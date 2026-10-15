using System;
using System.Collections.Generic;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// INPUT-01: which kOS window owns the keyboard and holds a control lock, for the
    /// policy agreed with the user (docs/KOSIm-roadmap.md, "INPUT-01 design").
    /// </summary>
    [TestFixture]
    public class InputOwnershipTest
    {
        /// <summary>Records lock calls like KSP's InputLockManager: one lock per name.</summary>
        private class RecordingSink : IInputLockSink
        {
            public readonly List<string> Calls = new List<string>();
            public readonly HashSet<string> Held = new HashSet<string>();
            public Func<InputOwnership.Window, string> LockName = w => w.Name;
            public string FailLockFor;
            public string FailUnlockFor;

            public void Lock(InputOwnership.Window window)
            {
                if (window.Name == FailLockFor)
                    throw new InvalidOperationException("lock refused");
                Calls.Add("lock " + window.Name);
                Held.Add(LockName(window));
            }

            public void Unlock(InputOwnership.Window window)
            {
                if (window.Name == FailUnlockFor)
                    throw new InvalidOperationException("unlock refused");
                Calls.Add("unlock " + window.Name);
                Held.Remove(LockName(window));
            }

            public string Take()
            {
                string result = string.Join(", ", Calls.ToArray());
                Calls.Clear();
                return result;
            }
        }

        private RecordingSink sink;
        private InputOwnership owner;
        private InputOwnership.Window termA;
        private InputOwnership.Window termB;
        private InputOwnership.Window gui;

        [SetUp]
        public void SetUp()
        {
            sink = new RecordingSink();
            owner = new InputOwnership(sink);
            termA = owner.Register("A", true);
            termB = owner.Register("B", true);
            gui = owner.Register("gui", false);
        }

        [Test]
        public void ANewWindowHasNoFocusAndNoLock()
        {
            Assert.IsNull(owner.KeyboardOwner);
            Assert.IsFalse(termA.IsOpen);
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void AUserOpenTakesFocusAndTheLock()
        {
            owner.Open(termA, true);
            Assert.IsTrue(owner.HasKeyboard(termA));
            Assert.IsTrue(termA.HoldsLock);
            Assert.AreEqual("lock A", sink.Take());
        }

        [Test]
        public void AnOpenByAScriptOrActionGroupLeavesFocusAlone()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.Open(termB, false);
            Assert.IsTrue(termB.IsOpen);
            Assert.IsTrue(owner.HasKeyboard(termA), "an unfocused open took the keyboard");
            Assert.AreEqual("", sink.Take());

            owner.Close(termA);
            owner.Open(termA, false);
            Assert.IsNull(owner.KeyboardOwner, "an unfocused open must not take focus from nobody either");
        }

        [Test]
        public void FocusMovesUnlockingBeforeLockingASharedLockName()
        {
            sink.LockName = w => "kOSTerminal";
            owner.Open(termA, true);
            owner.Open(termB, false);
            sink.Take();

            owner.Click(termB);
            Assert.AreEqual("unlock A, lock B", sink.Take());
            CollectionAssert.AreEquivalent(new[] { "kOSTerminal" }, sink.Held);
            Assert.IsTrue(owner.HasKeyboard(termB));
            Assert.IsFalse(termA.WantsFocus);
        }

        [Test]
        public void AClickOutsideReleases()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.Click(null);
            Assert.IsNull(owner.KeyboardOwner);
            Assert.AreEqual("unlock A", sink.Take());
        }

        [Test]
        public void AClickOnAClosedWindowCountsAsOutside()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.Click(termB);
            Assert.IsNull(owner.KeyboardOwner);
            Assert.AreEqual("unlock A", sink.Take());
        }

        [Test]
        public void AScriptGuiWindowTakesTheKeyboardWithoutALock()
        {
            owner.Open(termA, true);
            owner.Open(gui, false);
            sink.Take();
            owner.Click(gui);
            Assert.IsTrue(owner.HasKeyboard(gui), "terminal keys would still be read while typing in a GUI field");
            Assert.AreEqual("unlock A", sink.Take());
        }

        [Test]
        public void ClosingTheOwnerReleasesAndGivesFocusToNobody()
        {
            owner.Open(termA, true);
            owner.Open(termB, false);
            sink.Take();
            owner.Close(termA);
            Assert.IsNull(owner.KeyboardOwner);
            Assert.AreEqual("unlock A", sink.Take());

            owner.Open(termA, false);
            Assert.IsNull(owner.KeyboardOwner, "focus came back after a close");
        }

        [Test]
        public void SuspensionReleasesAndFocusComesBack()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.SetSuspended(true);
            Assert.IsNull(owner.KeyboardOwner, "a hidden terminal still read keys");
            Assert.AreEqual("unlock A", sink.Take());

            owner.SetSuspended(false);
            Assert.IsTrue(owner.HasKeyboard(termA));
            Assert.AreEqual("lock A", sink.Take());
        }

        [Test]
        public void RepeatingTheSameSuspensionStateDoesNothing()
        {
            owner.Open(termA, true);
            sink.Take();
            for (int i = 0; i < 3; ++i)
                owner.SetSuspended(false);
            owner.SetSuspended(true);
            owner.SetSuspended(true);
            Assert.AreEqual("unlock A", sink.Take());
        }

        [Test]
        public void AUserOpenWhileSuspendedGetsFocusWhenTheUiReturns()
        {
            owner.Open(termA, true);
            owner.SetSuspended(true);
            owner.Open(termB, true);
            sink.Take();
            Assert.IsNull(owner.KeyboardOwner);

            owner.SetSuspended(false);
            Assert.IsTrue(owner.HasKeyboard(termB));
            Assert.AreEqual("lock B", sink.Take());
        }

        [Test]
        public void AWindowClosedWhileSuspendedDoesNotComeBack()
        {
            owner.Open(termA, true);
            owner.SetSuspended(true);
            owner.Close(termA);
            sink.Take();
            owner.SetSuspended(false);
            Assert.IsNull(owner.KeyboardOwner);
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void PowerLossReleasesForGood()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.SetPowered(termA, false);
            Assert.IsNull(owner.KeyboardOwner);
            Assert.AreEqual("unlock A", sink.Take());

            owner.Click(termA);
            Assert.IsNull(owner.KeyboardOwner, "an unpowered terminal took focus");
            owner.SetPowered(termA, true);
            Assert.IsNull(owner.KeyboardOwner, "power return took focus back");
            Assert.AreEqual("", sink.Take());

            owner.Click(termA);
            Assert.IsTrue(owner.HasKeyboard(termA));
            Assert.AreEqual("lock A", sink.Take());
        }

        [Test]
        public void PowerLossOfAnotherWindowKeepsTheOwner()
        {
            owner.Open(termA, true);
            owner.Open(termB, false);
            sink.Take();
            owner.SetPowered(termB, false);
            Assert.IsTrue(owner.HasKeyboard(termA));
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void PowerLossWhileSuspendedDoesNotComeBack()
        {
            owner.Open(termA, true);
            owner.SetSuspended(true);
            owner.SetPowered(termA, false);
            owner.SetPowered(termA, true);
            owner.SetSuspended(false);
            Assert.IsNull(owner.KeyboardOwner);
        }

        [Test]
        public void UnregisterReleasesAndIsIdempotent()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.Unregister(termA);
            owner.Unregister(termA);
            Assert.AreEqual("unlock A", sink.Take());
            Assert.IsNull(owner.KeyboardOwner);
            Assert.Throws<InvalidOperationException>(() => owner.Open(termA, true));
        }

        [Test]
        public void UnregisterOfAnotherWindowChangesNothing()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.Unregister(termB);
            Assert.IsTrue(owner.HasKeyboard(termA));
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void ReleaseAllRemovesEveryLockAndIsIdempotent()
        {
            owner.Open(termA, true);
            owner.Open(termB, false);
            sink.Take();
            owner.ReleaseAll();
            owner.ReleaseAll();
            Assert.AreEqual("unlock A", sink.Take());
            CollectionAssert.IsEmpty(sink.Held);
            owner.SetSuspended(true);
            owner.SetSuspended(false);
            Assert.IsNull(owner.KeyboardOwner, "focus came back after a scene change");
        }

        [Test]
        public void RepeatedClicksOnTheOwnerDoNothing()
        {
            owner.Open(termA, true);
            sink.Take();
            owner.Click(termA);
            owner.Open(termA, true);
            owner.Apply();
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void ARefusedLockIsRetriedAndReported()
        {
            sink.FailLockFor = "A";
            Assert.Throws<InvalidOperationException>(() => owner.Open(termA, true));
            Assert.IsFalse(termA.HoldsLock);
            Assert.IsTrue(owner.HasKeyboard(termA), "the focus decision stands; only the lock is pending");

            sink.FailLockFor = null;
            owner.Apply();
            Assert.IsTrue(termA.HoldsLock);
            Assert.AreEqual("lock A", sink.Take());
        }

        [Test]
        public void ARefusedUnlockDelaysTheNextLockUntilItSucceeds()
        {
            // With a shared lock name, locking B and then retrying A's unlock would remove B's lock.
            sink.LockName = w => "kOSTerminal";
            owner.Open(termA, true);
            owner.Open(termB, false);
            sink.Take();
            sink.FailUnlockFor = "A";
            Assert.Throws<InvalidOperationException>(() => owner.Click(termB));
            Assert.IsTrue(termA.HoldsLock);
            Assert.IsFalse(termB.HoldsLock);
            Assert.AreEqual("", sink.Take());

            sink.FailUnlockFor = null;
            owner.Apply();
            Assert.IsFalse(termA.HoldsLock);
            Assert.IsTrue(termB.HoldsLock);
            Assert.AreEqual("unlock A, lock B", sink.Take());
            CollectionAssert.AreEquivalent(new[] { "kOSTerminal" }, sink.Held);
        }

        [Test]
        public void UnregisterRemovesTheWindowEvenWhenTheUnlockFails()
        {
            owner.Open(termA, true);
            sink.FailUnlockFor = "A";
            Assert.Throws<InvalidOperationException>(() => owner.Unregister(termA));
            sink.FailUnlockFor = null;
            sink.Take();
            owner.Apply();
            Assert.AreEqual("", sink.Take(), "a removed window was still managed");
            owner.Unregister(termA);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new InputOwnership(null));
            Assert.Throws<ArgumentNullException>(() => owner.Open(null, true));
            Assert.Throws<ArgumentNullException>(() => owner.Close(null));
            Assert.Throws<ArgumentNullException>(() => owner.SetPowered(null, true));
            owner.Unregister(null);
            owner.Click(null);
        }
    }
}
