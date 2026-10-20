using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 4, step 3c: the fonts the GUI terminal tries. The first one the OS has is used, so
    /// these tests pick from the font lists the step 3a probe found (Proton) and the ones
    /// upstream was written for (Windows, macOS, Linux).
    /// </summary>
    [TestFixture]
    public class TerminalFontChoiceTest
    {
        /// <summary>The upstream list after the user's pick (<c>TermWindow.tryFontNames</c> before step 3c).</summary>
        private static readonly string[] upstreamFallbacks =
        {
            "Consolas Bold", "Consolas", "Monaco Bold", "Monaco", "Liberation Mono Bold", "Liberation Mono",
            "Courier New Bold", "Courier Bold", "Courier New", "Courier", "Arial"
        };

        /// <summary>The Latin fonts of the dev instance's Proton prefix (step 3a probe), Arial for Unity's built-in.</summary>
        private static readonly string[] protonFonts = { "Arial", "Courier New", "Courier New Bold", "Lucida Console", "Tahoma", "Times New Roman" };

        /// <summary>As the game does: the first name the OS has.</summary>
        private static string FirstInstalled(IEnumerable<string> names, IEnumerable<string> installed)
        {
            var have = new HashSet<string>(installed);
            return names.FirstOrDefault(have.Contains);
        }

        [Test]
        public void APickedFontComesFirst()
        {
            string[] names = TerminalFontChoice.NamesToTry("Courier New Bold");
            Assert.AreEqual("Courier New Bold", names[0]);
            CollectionAssert.AreEqual(TerminalFontChoice.Fallbacks, names.Skip(1).ToArray());
            Assert.AreEqual("Courier New Bold", FirstInstalled(names, protonFonts), "a picked Courier New Bold is kept under Proton");
            Assert.AreEqual("Some Font", TerminalFontChoice.NamesToTry("Some Font")[0]);
        }

        [Test]
        public void NoPickIsNotTried()
        {
            foreach (string unpicked in new[] { TerminalFontChoice.NOT_CHOSEN_YET, TerminalFontChoice.CHOOSE_FONT, "", "  ", null })
            {
                Assert.IsFalse(TerminalFontChoice.IsPicked(unpicked), "[" + unpicked + "]");
                CollectionAssert.AreEqual(TerminalFontChoice.Fallbacks, TerminalFontChoice.NamesToTry(unpicked), "[" + unpicked + "]");
            }
            // The values the game writes (Config's default, Bootstrapper.CheckForUnpickedFont):
            Assert.AreEqual("_not_chosen_yet_", TerminalFontChoice.NOT_CHOSEN_YET);
            Assert.AreEqual("Choose Font", TerminalFontChoice.CHOOSE_FONT);
        }

        [Test]
        public void TheFallbacksAreUpstreamsWithLucidaConsoleBeforeCourier()
        {
            List<string> fallbacks = TerminalFontChoice.Fallbacks.ToList();
            int lucida = fallbacks.IndexOf(TerminalFontChoice.LUCIDA_CONSOLE);
            Assert.GreaterOrEqual(lucida, 0);
            Assert.AreEqual(1, fallbacks.Count(name => name == TerminalFontChoice.LUCIDA_CONSOLE));
            CollectionAssert.AreEqual(upstreamFallbacks, fallbacks.Where(name => name != TerminalFontChoice.LUCIDA_CONSOLE).ToArray(),
                "otherwise upstream's names in upstream's order");
            int firstCourier = fallbacks.FindIndex(name => name.StartsWith("Courier", StringComparison.Ordinal));
            Assert.AreEqual(firstCourier - 1, lucida, "just before the Courier entries");
            Assert.AreEqual("Arial", fallbacks.Last());
        }

        [Test]
        public void ProtonGetsLucidaConsole()
        {
            Assert.AreEqual("Lucida Console", FirstInstalled(TerminalFontChoice.NamesToTry(TerminalFontChoice.CHOOSE_FONT), protonFonts));
            Assert.AreEqual("Courier New Bold", FirstInstalled(upstreamFallbacks, protonFonts), "upstream got Courier New Bold");
        }

        [Test]
        public void OtherSystemsKeepWhatTheyHad()
        {
            var systems = new Dictionary<string, string[]>
            {
                { "Windows", new[] { "Arial", "Consolas", "Consolas Bold", "Courier New", "Courier New Bold", "Lucida Console" } },
                { "macOS", new[] { "Arial", "Monaco", "Courier", "Courier New", "Courier New Bold" } },
                { "Linux", new[] { "Arial", "Liberation Mono", "Liberation Mono Bold", "DejaVu Sans Mono" } },
                { "Linux without Liberation", new[] { "Arial", "DejaVu Sans Mono", "Courier New" } },
                { "nothing but Arial", new[] { "Arial" } }
            };
            foreach (KeyValuePair<string, string[]> system in systems)
                Assert.AreEqual(FirstInstalled(upstreamFallbacks, system.Value),
                    FirstInstalled(TerminalFontChoice.NamesToTry(TerminalFontChoice.CHOOSE_FONT), system.Value), system.Key);
        }

        [Test]
        public void EachCallGivesItsOwnList()
        {
            string[] first = TerminalFontChoice.NamesToTry("A Font");
            first[0] = "changed";
            Assert.AreEqual("A Font", TerminalFontChoice.NamesToTry("A Font")[0]);
            Assert.AreEqual(upstreamFallbacks.Length + 1, TerminalFontChoice.Fallbacks.Count);
        }
    }
}
