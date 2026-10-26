using System;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// The toolbar button's right click in flight (user decision 2026-10-26): the active
    /// vessel's terminals open when any is closed, else close; windows without a place of
    /// their own are stepped apart.
    /// </summary>
    [TestFixture]
    public class VesselTerminalsTest
    {
        [Test]
        public void AnyClosedTerminalOpensThemAllElseTheyAllClose()
        {
            Assert.IsTrue(VesselTerminals.OpensAll(new[] { false, false }), "all closed");
            Assert.IsTrue(VesselTerminals.OpensAll(new[] { true, false, true }), "one closed: the rest is opened, not closed");
            Assert.IsFalse(VesselTerminals.OpensAll(new[] { true, true }), "all open: the second click closes them");
            Assert.IsFalse(VesselTerminals.OpensAll(new bool[0]), "no terminal: nothing to open");
            Assert.Throws<ArgumentNullException>(() => VesselTerminals.OpensAll(null));
        }

        [Test]
        public void WindowsAtTheDefaultPlaceAreSteppedApart()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2 },
                VesselTerminals.CascadeSteps(new[] { false, false, false }, new[] { true, true, true }),
                "the first stays at the default place");
        }

        [Test]
        public void AWindowWithItsOwnPlaceKeepsIt()
        {
            CollectionAssert.AreEqual(new[] { 0, -1, 1 },
                VesselTerminals.CascadeSteps(new[] { false, false, false }, new[] { true, false, true }));
        }

        [Test]
        public void WindowsAlreadyOpenAtTheDefaultPlaceAreNotCovered()
        {
            CollectionAssert.AreEqual(new[] { -1, 1, -1, 2 },
                VesselTerminals.CascadeSteps(new[] { true, false, true, false }, new[] { true, true, false, true }),
                "one already open there: the opened ones go after it; open windows do not move");
        }

        [Test]
        public void TheListsMustMatch()
        {
            Assert.Throws<ArgumentException>(() => VesselTerminals.CascadeSteps(new[] { false }, new[] { true, true }));
            Assert.Throws<ArgumentNullException>(() => VesselTerminals.CascadeSteps(null, new bool[0]));
            Assert.Throws<ArgumentNullException>(() => VesselTerminals.CascadeSteps(new bool[0], null));
        }
    }
}
