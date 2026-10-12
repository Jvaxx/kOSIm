using System;
using System.Threading;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Per-view render retry/report gate. Call on the view's owning thread with monotonic
    /// seconds. It retains no exception objects and never repairs or clears model data.
    /// </summary>
    public sealed class RenderFaultRecovery
    {
        private readonly double retryDelaySeconds;
        private readonly double reportIntervalSeconds;
        private double nextAttempt = double.NegativeInfinity;
        private double nextReport = double.NegativeInfinity;

        public bool IsFaulted { get; private set; }
        public int SuppressedFailures { get; private set; }

        public RenderFaultRecovery(double retryDelaySeconds = 2, double reportIntervalSeconds = 10)
        {
            if (!IsPositiveFinite(retryDelaySeconds)) throw new ArgumentOutOfRangeException("retryDelaySeconds");
            if (!IsPositiveFinite(reportIntervalSeconds)) throw new ArgumentOutOfRangeException("reportIntervalSeconds");
            this.retryDelaySeconds = retryDelaySeconds;
            this.reportIntervalSeconds = reportIntervalSeconds;
        }

        /// <summary>
        /// Try drawing, or return false during cooldown. Nonfatal recoverable failures
        /// return false and periodically report the original exception plus the number
        /// of failed attempts since the last report. Success does not reset the report
        /// budget: intermittent or changing errors must not produce repaint log spam.
        /// The caller must render its fallback outside this boundary.
        /// </summary>
        public bool TryRender(double now, Action draw, Func<Exception, bool> canRecover, Action<Exception, int> report)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException("now");
            if (draw == null) throw new ArgumentNullException("draw");
            if (canRecover == null) throw new ArgumentNullException("canRecover");
            if (report == null) throw new ArgumentNullException("report");
            if (now < nextAttempt) return false;

            try
            {
                draw();
            }
            catch (Exception exception)
            {
                if (IsFatal(exception) || !canRecover(exception)) throw;

                IsFaulted = true;
                nextAttempt = now + retryDelaySeconds;
                if (now >= nextReport)
                {
                    int suppressed = SuppressedFailures;
                    SuppressedFailures = 0;
                    nextReport = now + reportIntervalSeconds;
                    // Reporting errors deliberately propagate; don't recursively catch them.
                    report(exception, suppressed);
                }
                else if (SuppressedFailures < int.MaxValue)
                    ++SuppressedFailures;
                return false;
            }

            IsFaulted = false;
            nextAttempt = double.NegativeInfinity;
            return true;
        }

        /// <summary>Allow an explicit retry without resetting the diagnostic rate limit.</summary>
        public void RequestRetry()
        {
            nextAttempt = double.NegativeInfinity;
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFatal(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is OutOfMemoryException || current is StackOverflowException ||
                    current is AccessViolationException || current is ThreadAbortException ||
                    current is AppDomainUnloadedException)
                    return true;
                var aggregate = current as AggregateException;
                if (aggregate != null)
                    foreach (Exception inner in aggregate.InnerExceptions)
                        if (IsFatal(inner)) return true;
            }
            return false;
        }
    }
}
