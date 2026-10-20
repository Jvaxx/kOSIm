using System;
using System.Reflection;
using NUnit.Framework;

namespace kOS.Safe.Test
{
    /// <summary>
    /// Counts the bytes the current thread allocates, so a test can show that a per-frame path
    /// allocates nothing (or less than before) instead of assuming it (review P-01 to P-04).
    /// Uses <c>GC.GetAllocatedBytesForCurrentThread</c>, which the headless gate's Mono has
    /// but the net48 reference assemblies do not declare, hence the reflection.
    /// </summary>
    internal static class AllocationCounter
    {
        private static readonly Func<long> allocatedBytes = FindCounter();

        private static Func<long> FindCounter()
        {
            MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        }

        /// <summary>
        /// The bytes <paramref name="action"/> allocates when run <paramref name="times"/> times,
        /// after one run to warm it up (JIT, lazy statics).
        /// </summary>
        public static long BytesAllocated(Action action, int times = 100)
        {
            if (allocatedBytes == null)
                Assert.Fail("This runtime has no per-thread allocation counter (GC.GetAllocatedBytesForCurrentThread); the headless gate's Mono 6.12 has one.");
            action();
            long before = allocatedBytes();
            for (int i = 0; i < times; ++i)
                action();
            return allocatedBytes() - before;
        }
    }
}
