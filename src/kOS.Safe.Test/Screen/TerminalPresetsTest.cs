using System;
using System.Collections.Generic;
using System.IO;
using kOS.Safe.Encapsulation;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// The terminal's geometry presets: three slots of complete geometries in one file for the
    /// game. The file is as tolerant as the per-CPU geometry file, and a slot with any value
    /// missing or not believed is empty, so a preset never applies half a geometry.
    /// </summary>
    [TestFixture]
    public class TerminalPresetsTest
    {
        private string directory;
        private ILogger previousLogger;
        private WarningLogger logger;

        private sealed class WarningLogger : ILogger
        {
            public readonly List<string> Warnings = new List<string>();
            public void Log(string text) { }
            public void Log(Exception e) { }
            public void SuperVerbose(string s) { }
            public void LogWarning(string s) { Warnings.Add(s); }
            public void LogWarningAndScreen(string s) { Warnings.Add(s); }
            public void LogException(Exception exception) { }
            public void LogError(string s) { }
        }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "kosim-presets-" + Guid.NewGuid().ToString("N"));
            previousLogger = SafeHouse.Logger;
            logger = new WarningLogger();
            SafeHouse.Logger = logger;
        }

        [TearDown]
        public void TearDown()
        {
            SafeHouse.Logger = previousLogger;
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private string FilePath { get { return Path.Combine(directory, "PluginData", "kOS", "terminal-presets.txt"); } }

        private static TerminalGeometry Geometry(int x, int columns)
        {
            return new TerminalGeometry { X = x, Y = 80, Columns = columns, Rows = 36, CharacterHeight = 14 };
        }

        private static string Slot(int slot, int x, int y, int columns, int rows, int charHeight)
        {
            return string.Format("{0}.x = {1}\n{0}.y = {2}\n{0}.columns = {3}\n{0}.rows = {4}\n{0}.charheight = {5}\n",
                slot, x, y, columns, rows, charHeight);
        }

        // The slots:

        [Test]
        public void SlotsHoldCopiesOfCompleteGeometries()
        {
            var presets = new TerminalPresets();
            for (int slot = 1; slot <= TerminalPresets.SLOT_COUNT; ++slot)
            {
                Assert.IsFalse(presets.IsFilled(slot), "empty at first: " + slot);
                Assert.IsNull(presets.Get(slot));
            }
            TerminalGeometry given = Geometry(120, 50);
            presets.Set(2, given);
            given.Columns = 99;
            Assert.IsTrue(presets.IsFilled(2));
            Assert.AreEqual(Geometry(120, 50), presets.Get(2), "the slot kept its own copy");
            presets.Get(2).Rows = 4;
            Assert.AreEqual(Geometry(120, 50), presets.Get(2), "a geometry got is a copy");
            Assert.IsFalse(presets.IsFilled(1));
            Assert.IsFalse(presets.IsFilled(3));
            presets.Set(2, null);
            Assert.IsFalse(presets.IsFilled(2), "null empties it");
        }

        [Test]
        public void ASlotTakesOnlyACompleteGeometryAndAKnownNumber()
        {
            var presets = new TerminalPresets();
            TerminalGeometry partial = Geometry(1, 50);
            partial.CharacterHeight = null;
            Assert.Throws<ArgumentException>(() => presets.Set(1, partial));
            Assert.IsFalse(presets.IsFilled(1));
            foreach (int slot in new[] { 0, -1, TerminalPresets.SLOT_COUNT + 1 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => presets.IsFilled(slot), "slot " + slot);
                Assert.Throws<ArgumentOutOfRangeException>(() => presets.Get(slot), "slot " + slot);
                Assert.Throws<ArgumentOutOfRangeException>(() => presets.Set(slot, Geometry(1, 50)), "slot " + slot);
            }
        }

        [Test]
        public void IsCompleteAndCopy()
        {
            Assert.IsTrue(Geometry(1, 50).IsComplete);
            foreach (Action<TerminalGeometry> clear in new Action<TerminalGeometry>[] {
                g => g.X = null, g => g.Y = null, g => g.Columns = null, g => g.Rows = null, g => g.CharacterHeight = null })
            {
                TerminalGeometry geometry = Geometry(1, 50);
                clear(geometry);
                Assert.IsFalse(geometry.IsComplete, geometry.ToString());
                Assert.AreEqual(geometry, geometry.Copy());
            }
            TerminalGeometry original = Geometry(1, 50);
            Assert.AreNotSame(original, original.Copy());
        }

        // The text:

        [Test]
        public void FormatThenParseGivesTheSamePresets()
        {
            var presets = new TerminalPresets();
            presets.Set(1, Geometry(120, 50));
            presets.Set(3, new TerminalGeometry { X = -40, Y = 0, Columns = 255, Rows = 160, CharacterHeight = 48 });
            string text = TerminalPresets.Format(presets);
            Assert.AreEqual(TerminalPresets.HEADER + "\n" + Slot(1, 120, 80, 50, 36, 14) + Slot(3, -40, 0, 255, 160, 48), text,
                "a header, then the filled slots' values after their numbers");
            TerminalPresets read = TerminalPresets.Parse(text);
            Assert.AreEqual(Geometry(120, 50), read.Get(1));
            Assert.IsFalse(read.IsFilled(2));
            Assert.AreEqual(presets.Get(3), read.Get(3));
            Assert.AreEqual(TerminalPresets.HEADER + "\n", TerminalPresets.Format(new TerminalPresets()), "no slot filled");
        }

        [Test]
        public void ParseIsTolerant()
        {
            TerminalPresets presets = TerminalPresets.Parse(
                "# a comment\r\n\r\n 2.X=10 \r\n2.y = 20\r\n2 . Columns =  40\r\n2.rows = 10\r\n2.rows = 12\r\n2.charheight=16\r\n" +
                "2.unknown = 3\r\nunknown = 3\r\nx = 5\r\n.x = 5\r\n4.x = 5\r\n0.x = 5\r\n-1.x = 5\r\na.x = 5\r\nno equals sign\r\n= 5\r\n");
            Assert.AreEqual(new TerminalGeometry { X = 10, Y = 20, Columns = 40, Rows = 12, CharacterHeight = 16 }, presets.Get(2),
                "CR LF, spaces, case; the last of a repeated key wins; unknown keys and slot numbers ignored");
            Assert.IsFalse(presets.IsFilled(1));
            Assert.IsFalse(presets.IsFilled(3));
            Assert.IsFalse(TerminalPresets.Parse("").IsFilled(1), "an empty file has no preset");
        }

        [Test]
        public void ASlotWithAValueMissingOrNotBelievedIsEmpty()
        {
            string whole = Slot(1, 120, 80, 50, 36, 14);
            Assert.IsTrue(TerminalPresets.Parse(whole).IsFilled(1));
            foreach (string key in new[] { "x", "y", "columns", "rows", "charheight" })
            {
                string missing = whole.Replace("1." + key + " = ", "1.gone = ");
                Assert.IsFalse(TerminalPresets.Parse(missing).IsFilled(1), "without " + key);
            }
            foreach (string bad in new[] { "1.x = 1.5", "1.y = 999999", "1.columns = 14", "1.rows = abc", "1.charheight = 49",
                "1.columns = " + (TerminalStruct.MAXCOLUMNS + 1), "1.rows = 99999999999" })
                Assert.IsFalse(TerminalPresets.Parse(whole + bad + "\n").IsFilled(1), "the last value not believed: " + bad);
            TerminalPresets mixed = TerminalPresets.Parse(whole + Slot(2, 1, 2, 30, 20, 12).Replace("2.rows = 20", "2.rows = 0"));
            Assert.IsTrue(mixed.IsFilled(1), "another slot's damage does not empty this one");
            Assert.IsFalse(mixed.IsFilled(2));
        }

        [Test]
        public void TheGeometryFileReadsAsBefore()
        {
            // The per-CPU file shares the presets' value checks; its own keys are unchanged.
            Assert.AreEqual(Geometry(120, 50), TerminalGeometry.Parse("x = 120\ny = 80\ncolumns = 50\nrows = 36\ncharheight = 14"));
            Assert.AreEqual(new TerminalGeometry(), TerminalGeometry.Parse(Slot(1, 120, 80, 50, 36, 14)), "preset keys are not the geometry's");
            Assert.IsFalse(TerminalPresets.Parse(TerminalGeometry.Format(Geometry(120, 50))).IsFilled(1), "nor the geometry's keys a preset's");
            Assert.Throws<ArgumentNullException>(() => TerminalGeometry.Parse(null));
            Assert.Throws<ArgumentNullException>(() => TerminalPresets.Parse(null));
        }

        // The file:

        [Test]
        public void SaveThenReadInANewStore()
        {
            var store = new TerminalPresetFile(() => FilePath);
            Assert.IsFalse(store.IsFilled(1), "no file yet");
            Assert.IsTrue(store.Save(1, Geometry(120, 50)));
            Assert.IsTrue(store.Save(3, Geometry(300, 80)));
            Assert.AreEqual(FilePath, store.Path);
            Assert.AreEqual(Geometry(120, 50), store.Get(1));

            var again = new TerminalPresetFile(() => FilePath);
            Assert.AreEqual(Geometry(120, 50), again.Get(1), "every slot is written at each save");
            Assert.IsFalse(again.IsFilled(2));
            Assert.AreEqual(Geometry(300, 80), again.Get(3));
            Assert.AreNotEqual(0xEF, File.ReadAllBytes(FilePath)[0], "no byte order mark");
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(FilePath), "*.tmp"), "no temporary file left");
            CollectionAssert.IsEmpty(logger.Warnings);
        }

        [Test]
        public void TheFileIsReadOnceAndKept()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, Slot(2, 10, 20, 40, 12, 16));
            int asked = 0;
            var store = new TerminalPresetFile(() => { ++asked; return FilePath; });
            Assert.AreEqual(0, asked, "nothing read before first use");
            Assert.IsTrue(store.IsFilled(2));
            File.WriteAllText(FilePath, Slot(1, 1, 2, 30, 20, 12));
            Assert.IsTrue(store.IsFilled(2), "kept, not read again");
            Assert.IsFalse(store.IsFilled(1));
            Assert.IsTrue(store.Save(3, Geometry(5, 60)));
            TerminalPresets onDisk = TerminalPresets.Parse(File.ReadAllText(FilePath));
            Assert.IsFalse(onDisk.IsFilled(1), "a save writes what the store holds");
            Assert.IsTrue(onDisk.IsFilled(2));
            Assert.IsTrue(onDisk.IsFilled(3));
            Assert.AreEqual(1, asked, "the path is asked once");
        }

        [Test]
        public void NoPathKeepsThePresetsForTheGameOnly()
        {
            var store = new TerminalPresetFile(() => null);
            Assert.IsFalse(store.Save(1, Geometry(120, 50)), "not saved");
            Assert.AreEqual(Geometry(120, 50), store.Get(1), "still set");
            CollectionAssert.IsEmpty(logger.Warnings);
        }

        private sealed class FailingReplace : ArchiveFileOperations
        {
            internal override void Replace(string source, string destination) { throw new IOException("replace refused"); }
        }

        [Test]
        public void AFailedSaveSaysSoKeepsTheOldFileAndTheSlot()
        {
            Assert.IsTrue(new TerminalPresetFile(() => FilePath).Save(1, Geometry(120, 50)));
            string before = File.ReadAllText(FilePath);
            var store = new TerminalPresetFile(() => FilePath, new FailingReplace());
            Assert.IsFalse(store.Save(2, Geometry(7, 30)));
            Assert.IsFalse(store.Save(2, Geometry(8, 30)));
            Assert.AreEqual(Geometry(8, 30), store.Get(2), "the slot is set for this game");
            Assert.AreEqual(Geometry(120, 50), store.Get(1));
            Assert.AreEqual(before, File.ReadAllText(FilePath), "the old file is kept");
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(FilePath), "*.tmp"));
            Assert.AreEqual(1, logger.Warnings.Count, "logged once, not per save");
            StringAssert.Contains("Terminal presets", logger.Warnings[0]);
            StringAssert.Contains("replace refused", logger.Warnings[0]);
        }

        [Test]
        public void AnOversizedFileIsNotRead()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, Slot(1, 120, 80, 50, 36, 14) + new string('#', (int)TerminalPresetFile.MAX_FILE_BYTES));
            var store = new TerminalPresetFile(() => FilePath);
            Assert.IsFalse(store.IsFilled(1));
            Assert.AreEqual(1, logger.Warnings.Count);
            StringAssert.Contains("Terminal presets", logger.Warnings[0]);
        }

        [Test]
        public void SaveTakesOnlyACompleteGeometry()
        {
            var store = new TerminalPresetFile(() => FilePath);
            Assert.Throws<ArgumentNullException>(() => store.Save(1, null));
            Assert.Throws<ArgumentException>(() => store.Save(1, new TerminalGeometry { Columns = 50 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(4, Geometry(1, 50)));
            Assert.IsFalse(File.Exists(FilePath), "nothing written");
        }
    }

    /// <summary>
    /// The preset buttons in the terminal's header: three buttons the size of the others, left
    /// of the smaller text button after a gap, shown only while the title keeps its least room.
    /// </summary>
    [TestFixture]
    public class TerminalPresetLayoutTest
    {
        private static readonly float[] scales = { 0.5f, 1f, 1.3f, 2f, 3f };

        private static void AssertApart(ScreenRect a, ScreenRect b, string what)
        {
            bool apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
            Assert.IsTrue(apart, what + ": " + a + " and " + b);
        }

        [Test]
        public void TheButtonsSitInOrderLeftOfTheSizeButtonsAfterAGap()
        {
            foreach (float scale in scales)
            {
                var layout = new TerminalChromeLayout(scale, 8, 14);
                for (float width = 0; width < 1200; width += 3.5f)
                {
                    if (!layout.ShowsPresets(width)) continue;
                    string where = "scale " + scale + ", width " + width;
                    ScreenRect smaller = layout.SmallerTextButton(width), header = layout.Header(width), title = layout.Title(width);
                    ScreenRect one = layout.PresetButton(1, width), two = layout.PresetButton(2, width), three = layout.PresetButton(3, width);
                    foreach (ScreenRect button in new[] { one, two, three })
                    {
                        Assert.AreEqual(layout.ButtonWidth, button.Width, where + ": as wide as the others");
                        Assert.AreEqual(layout.HeaderHeight, button.Height, where);
                        Assert.AreEqual(0, button.Y, where);
                        Assert.GreaterOrEqual(button.X, header.X, where);
                        AssertApart(title, button, where + ": title and a preset");
                        foreach (ScreenRect other in new[] { smaller, layout.LargerTextButton(width), layout.CloseButton(width) })
                            AssertApart(button, other, where + ": a preset and a size or close button");
                    }
                    Assert.AreEqual(one.X + one.Width, two.X, where + ": 1 then 2");
                    Assert.AreEqual(two.X + two.Width, three.X, where + ": 2 then 3");
                    Assert.Greater(layout.PresetGap, 0, where);
                    Assert.AreEqual(layout.PresetGap, smaller.X - (three.X + three.Width), where + ": the gap before the smaller text button");
                    Assert.AreEqual(one.X, layout.HeaderButtons(width).X, where + ": the header's buttons start at preset 1");
                    Assert.AreEqual(width - one.X, layout.HeaderButtons(width).Width, where);
                    Assert.GreaterOrEqual(title.Width, layout.MinimumTitleWithPresets, where + ": the title keeps its room");
                }
            }
        }

        [Test]
        public void TheButtonsHideInANarrowWindow()
        {
            // At scale 1: six buttons of 20, a gap of 6, padding of 6 either side of a 60 pixel title.
            var layout = new TerminalChromeLayout(1f, 8, 14);
            Assert.IsTrue(layout.ShowsPresets(198));
            Assert.AreEqual(60, layout.Title(198).Width);
            Assert.IsFalse(layout.ShowsPresets(197.5f));
            Assert.IsFalse(layout.ShowsPresets(layout.MinimumWindowWidth), "the 15-column window");
            Assert.IsTrue(layout.ShowsPresets(layout.WindowWidthFor(50)), "the default 50 columns");

            foreach (float scale in new[] { 1f, 1.3f, 2f })
            {
                layout = new TerminalChromeLayout(scale, 8, 14);
                float threshold = 6 * layout.ButtonWidth + layout.PresetGap + 2 * layout.Padding + layout.MinimumTitleWithPresets;
                string where = "scale " + scale;
                Assert.IsTrue(layout.ShowsPresets(threshold), where + ": at the threshold");
                Assert.AreEqual(layout.MinimumTitleWithPresets, layout.Title(threshold).Width, where);
                Assert.IsFalse(layout.ShowsPresets(threshold - 0.5f), where + ": below it");
                foreach (float width in new[] { layout.MinimumWidth, threshold - 0.5f })
                {
                    Assert.AreEqual(layout.SmallerTextButton(width).X, layout.HeaderButtons(width).X, where + ", " + width + ": hidden, as before");
                    Assert.AreEqual(3 * layout.ButtonWidth, layout.HeaderButtons(width).Width, where + ", " + width);
                }
            }
        }

        [Test]
        public void TheButtonsAndTheGapAreNotAResizeEdge()
        {
            foreach (float scale in scales)
            {
                var layout = new TerminalChromeLayout(scale, 8, 14);
                float w = layout.WindowWidthFor(80), h = layout.WindowHeightFor(36);
                string where = "scale " + scale;
                Assert.IsTrue(layout.ShowsPresets(w), where);
                for (int slot = 1; slot <= TerminalPresets.SLOT_COUNT; ++slot)
                {
                    ScreenRect button = layout.PresetButton(slot, w);
                    Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, button.X, 0, false), where + ": preset " + slot);
                    Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, button.X + button.Width / 2, 1, false), where);
                }
                float gap = layout.SmallerTextButton(w).X - 1;
                Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, gap, 0, false), where + ": the gap");
                Assert.AreEqual(ResizeEdges.Top, TerminalResize.EdgesAt(layout, w, h, layout.PresetButton(1, w).X - 1, 0, false),
                    where + ": the top edge left of the presets");

                float narrow = 6 * layout.ButtonWidth + layout.PresetGap + 2 * layout.Padding + layout.MinimumTitleWithPresets - 1;
                Assert.IsFalse(layout.ShowsPresets(narrow), where);
                Assert.AreEqual(ResizeEdges.Top, TerminalResize.EdgesAt(layout, narrow, h, layout.SmallerTextButton(narrow).X - 1, 0, false),
                    where + ": hidden, the top edge reaches the size buttons");
            }
        }

        [Test]
        public void AnUnknownSlotHasNoButton()
        {
            var layout = new TerminalChromeLayout(1f, 8, 14);
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.PresetButton(0, 400));
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.PresetButton(TerminalPresets.SLOT_COUNT + 1, 400));
            for (int slot = 1; slot <= TerminalPresets.SLOT_COUNT; ++slot)
                Assert.GreaterOrEqual(layout.PresetButton(slot, 0).X, 0, "never left of the window");
        }
    }
}
