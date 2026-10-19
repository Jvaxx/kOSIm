using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace kOS.Safe.Execution
{
    public abstract class YiedFinishedThreadedDetector : YieldFinishedDetector
    {
        private ManualResetEvent childThreadEvent;
        private Thread childThread;
        private Exception childException;
        private bool cancelled;

        protected SafeSharedObjects shared;

        public override void Begin(SafeSharedObjects sharedObj)
        {
            if (childThread != null)
                throw new Exceptions.KOSException("Error calling Begin, childThread is not null.");

            shared = sharedObj;

            childThreadEvent = new ManualResetEvent(false);

            ThreadInitialize(sharedObj);

            childThread = new Thread(DoThread);
            childThread.IsBackground = true;
            childThread.Start();
        }

        public override bool IsFinished()
        {
            if (childThread == null)
                return true; // Already finished, or already reported its error.
            if (childThreadEvent.WaitOne(0))
            {
                childThread.Join();
                if (cancelled)
                {
                    // The work is over and nobody wants it: no ThreadFinish(), no error.
                    childThread = null;
                    childException = null;
                    ThreadCancelled();
                    return true;
                }
                if (childException == null)
                {
                    try
                    {
                        ThreadFinish();
                    }
                    catch (Exception ex)
                    {
                        childException = ex;
                    }
                }
                // Note this is *deliberately* NOT an "else" of the above "if" even though
                // it looks like it should be.  That is because the above IF clause can actually
                // alter this flag and if it does so it needs to fall through to here and do this.
                if (childException != null)
                {
                    // If there was an error in the child thread, break execution and then
                    // throw the exception.  Because we're still executing the same opcode
                    // throwing will be caught and logged exactly the same as any other error
                    // in normal code.  It's important to break execution to ensure the
                    // instruction pointer skips the current opcode that throws the exception.
                    // Taken and cleared first: the break cancels every yield, this one included,
                    // and Cancel() must then see it as already over.
                    Exception error = childException;
                    childException = null;
                    childThread = null;
                    shared.Cpu.BreakExecution(false);
                    // Rethrow with the stack trace from where it was first thrown, on the child
                    // thread or in ThreadFinish():
                    ExceptionDispatchInfo.Capture(error).Throw();
                }
                childThread = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Discard the result (a break, e.g. Ctrl-C). The child thread cannot be stopped, so
        /// once it ends <see cref="ThreadFinish"/> is skipped and any error is dropped.
        /// Returns true while the thread is still running: the caller must keep this
        /// detector, so that nothing else starts until the thread is done.
        /// </summary>
        public bool Cancel()
        {
            cancelled = true;
            if (childThread == null)
                return false; // Never started, or already seen to end.
            if (!childThreadEvent.WaitOne(0))
                return true;
            IsFinished(); // Ended but not yet seen: discard it now.
            return false;
        }

        /// <summary>True after <see cref="Cancel"/> until the main thread has seen the child thread end.</summary>
        public bool IsCancelledAndRunning { get { return cancelled && childThread != null; } }

        /// <summary>Called on the main thread instead of <see cref="ThreadFinish"/> after <see cref="Cancel"/>.</summary>
        public virtual void ThreadCancelled()
        {
        }

        private void DoThread()
        {
            try
            {
                ThreadExecute();
            }
            catch (Exception ex)
            {
                childException = ex;
            }
            childThreadEvent.Set();
        }

        /// <summary>
        /// This method is executed before starting the child thread.  It is called from the main thread and is not required
        /// to be thread safe with respect to KSP.
        /// </summary>
        /// <param name="shared"></param>
        public abstract void ThreadInitialize(SafeSharedObjects shared);

        /// <summary>
        /// <para>
        /// This method is what actually executes in the background.  It should do the majority of the work.
        /// Remember that the KSP and Unity APIs are not thread safe, so calls to either API should be avoided in this
        /// method.
        /// </para>
        /// <para>
        /// WARNING: THIS METHOD EXECUTES ON A SEPARATE THREAD.  TAKE CARE TO ENSURE THAT IMPLEMENTATIONS ARE THREAD SAFE
        /// </para>
        /// </summary>
        /// <param name="shared"></param>
        public abstract void ThreadExecute();

        /// <summary>
        /// This method is executed after the child thread is finished, when the CPU checks IsFinished.  It is called from
        /// the main thread and is not required to be thread safe with respect to KSP.
        /// </summary>
        public abstract void ThreadFinish();
    }
}
