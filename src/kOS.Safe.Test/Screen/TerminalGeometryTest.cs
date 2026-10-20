using System;
using System.Collections.Generic;
using System.IO;
using kOS.Safe.Encapsulation;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 4, step 2: a CPU's terminal geometry saved next to its history. The file is
    /// tolerant of hand edits and damage: a value it cannot believe is left to the default.
    /// </summary>
    [TestFixture]
    public class TerminalGeometryTest
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
            directory = Path.Combine(Path.GetTempPath(), "kosim-geometry-" + Guid.NewGuid().ToString("N"));
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

        private string FilePath { get { return Path.Combine(directory, "kOS", "terminal", "123.txt"); } }

        private static TerminalGeometry Full()
        {
            return new TerminalGeometry { X = 120, Y = -5, Columns = 80, Rows = 24, CharacterHeight = 14 };
        }

        // The text:

        [Test]
        public void FormatThenParseGivesTheSameGeometry()
        {
            TerminalGeometry geometry = Full();
            string text = TerminalGeometry.Format(geometry);
            StringAssert.StartsWith(TerminalGeometry.HEADER + "\n", text);
            StringAssert.Contains("columns = 80\n", text);
            Assert.AreEqual(geometry, TerminalGeometry.Parse(text));

            var partial = new TerminalGeometry { Columns = 50 };
            Assert.AreEqual("columns = 50\n", TerminalGeometry.Format(partial).Substring(TerminalGeometry.HEADER.Length + 1),
                "unknown values are not written");
            Assert.AreEqual(partial, TerminalGeometry.Parse(TerminalGeometry.Format(partial)));
        }

        [Test]
        public void ParseIsTolerant()
        {
            TerminalGeometry geometry = TerminalGeometry.Parse(
                "# a comment\r\n\r\n  X=10 \r\nY = 20\r\nColumns =  40\r\nunknown = 3\r\nno equals sign\r\n= 5\r\nrows = 10\r\nrows = 12\r\ncharheight=16");
            Assert.AreEqual(10, geometry.X);
            Assert.AreEqual(20, geometry.Y);
            Assert.AreEqual(40, geometry.Columns);
            Assert.AreEqual(12, geometry.Rows, "the last of a repeated key wins");
            Assert.AreEqual(16, geometry.CharacterHeight);
            Assert.AreEqual(new TerminalGeometry(), TerminalGeometry.Parse(""), "an empty file knows nothing");
        }

        [Test]
        public void ValuesThatCannotBeBelievedAreLeftUnknown()
        {
            TerminalGeometry geometry = TerminalGeometry.Parse(string.Join("\n", new[] {
                "x = 1.5", "y = 999999", "columns = 14", "rows = abc", "charheight = 49" }));
            Assert.IsNull(geometry.X, "not a whole number");
            Assert.IsNull(geometry.Y, "too far from the screen");
            Assert.IsNull(geometry.Columns, "under the script minimum");
            Assert.IsNull(geometry.Rows, "not a number");
            Assert.IsNull(geometry.CharacterHeight, "over CHARHEIGHT's maximum");

            geometry = TerminalGeometry.Parse(string.Join("\n", new[] {
                "x = -" + TerminalGeometry.MAX_COORDINATE, "y = 0",
                "columns = " + TerminalStruct.MAXCOLUMNS, "rows = " + TerminalStruct.MINROWS,
                "charheight = " + TerminalStruct.MINCHARPIXELS }));
            Assert.AreEqual(-TerminalGeometry.MAX_COORDINATE, geometry.X);
            Assert.AreEqual(TerminalStruct.MAXCOLUMNS, geometry.Columns, "the limits themselves are kept");
            Assert.AreEqual(TerminalStruct.MINROWS, geometry.Rows);
            Assert.AreEqual(TerminalStruct.MINCHARPIXELS, geometry.CharacterHeight);

            Assert.IsNull(TerminalGeometry.Parse("rows = " + (TerminalStruct.MAXROWS + 1)).Rows);
            Assert.IsNull(TerminalGeometry.Parse("columns = 99999999999").Columns, "overflow");
        }

        [Test]
        public void EqualityComparesEveryValue()
        {
            Assert.AreEqual(Full(), Full());
            Assert.AreEqual(Full().GetHashCode(), Full().GetHashCode());
            foreach (Action<TerminalGeometry> change in new Action<TerminalGeometry>[] {
                g => g.X = 121, g => g.Y = null, g => g.Columns = 81, g => g.Rows = 25, g => g.CharacterHeight = 12 })
            {
                TerminalGeometry other = Full();
                change(other);
                Assert.AreNotEqual(Full(), other, other.ToString());
            }
            Assert.IsFalse(Full().Equals(null));
        }

        // The file:

        [Test]
        public void SaveThenLoad()
        {
            var store = new TerminalGeometryFile(() => FilePath);
            Assert.IsNull(store.Load(), "no file yet");
            store.Save(Full());
            Assert.IsTrue(File.Exists(FilePath), "the folders are created");
            Assert.AreEqual(FilePath, store.Path);
            Assert.AreEqual(Full(), new TerminalGeometryFile(() => FilePath).Load());
            Assert.AreNotEqual(0xEF, File.ReadAllBytes(FilePath)[0], "no byte order mark");
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(FilePath), "*.tmp"), "no temporary file left");
            CollectionAssert.IsEmpty(logger.Warnings);
        }

        [Test]
        public void PathIsAskedOnceAndNullKeepsNothing()
        {
            int asked = 0;
            var store = new TerminalGeometryFile(() => { ++asked; return (string)null; });
            store.Save(Full());
            Assert.IsNull(store.Load());
            Assert.AreEqual(1, asked);
            CollectionAssert.IsEmpty(logger.Warnings);
        }

        [Test]
        public void AFailingPathIsLoggedOnceAndKeepsNothing()
        {
            var store = new TerminalGeometryFile(() => { throw new InvalidOperationException("no save folder"); });
            Assert.DoesNotThrow(() => store.Save(Full()));
            Assert.IsNull(store.Load());
            Assert.AreEqual(1, logger.Warnings.Count);
            StringAssert.Contains("Terminal geometry", logger.Warnings[0]);
        }

        private sealed class FailingReplace : ArchiveFileOperations
        {
            internal override void Replace(string source, string destination) { throw new IOException("replace refused"); }
        }

        [Test]
        public void AFailedSaveKeepsTheOldFileAndDoesNotThrow()
        {
            new TerminalGeometryFile(() => FilePath).Save(Full());
            var store = new TerminalGeometryFile(() => FilePath, new FailingReplace());
            Assert.DoesNotThrow(() => store.Save(new TerminalGeometry { Columns = 20 }));
            Assert.DoesNotThrow(() => store.Save(new TerminalGeometry { Columns = 30 }));
            Assert.AreEqual(Full(), new TerminalGeometryFile(() => FilePath).Load());
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(FilePath), "*.tmp"));
            Assert.AreEqual(1, logger.Warnings.Count, "logged once, not per save");
            StringAssert.Contains("replace refused", logger.Warnings[0]);
        }

        [Test]
        public void AnOversizedFileIsNotRead()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, TerminalGeometry.Format(Full()) + new string('#', (int)TerminalGeometryFile.MAX_FILE_BYTES));
            Assert.IsNull(new TerminalGeometryFile(() => FilePath).Load());
            Assert.AreEqual(1, logger.Warnings.Count);
        }

        [Test]
        public void ADamagedFileGivesWhatItCan()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllBytes(FilePath, new byte[] { 0x78, 0x20, 0x3D, 0x20, 0x37, 0x0A, 0xFF, 0xFE, 0x00, 0x0A, 0x72, 0x6F, 0x77 });
            TerminalGeometry geometry = new TerminalGeometryFile(() => FilePath).Load();
            Assert.AreEqual(7, geometry.X);
            Assert.IsNull(geometry.Rows);
        }
    }

    /// <summary>Phase 4, step 2: a window is kept on the screen without changing its size.</summary>
    [TestFixture]
    public class WindowPlacementTest
    {
        private static void AssertRect(float x, float y, float width, float height, kOS.Safe.UserIO.ScreenRect rect, string message)
        {
            Assert.AreEqual(x, rect.X, message + ": x");
            Assert.AreEqual(y, rect.Y, message + ": y");
            Assert.AreEqual(width, rect.Width, message + ": width");
            Assert.AreEqual(height, rect.Height, message + ": height");
        }

        private static kOS.Safe.UserIO.ScreenRect Rect(float x, float y, float width, float height)
        {
            return new kOS.Safe.UserIO.ScreenRect(x, y, width, height);
        }

        [Test]
        public void AWindowOnScreenStays()
        {
            AssertRect(50, 60, 400, 300, WindowPlacement.KeepOnScreen(Rect(50, 60, 400, 300), 1024, 768), "inside");
            AssertRect(0, 0, 1024, 768, WindowPlacement.KeepOnScreen(Rect(0, 0, 1024, 768), 1024, 768), "exactly the screen");
            AssertRect(624, 468, 400, 300, WindowPlacement.KeepOnScreen(Rect(624, 468, 400, 300), 1024, 768), "at the corner");
        }

        [Test]
        public void AWindowOffScreenComesBackTheLeastDistance()
        {
            AssertRect(0, 0, 400, 300, WindowPlacement.KeepOnScreen(Rect(-30, -1, 400, 300), 1024, 768), "above and left");
            AssertRect(624, 468, 400, 300, WindowPlacement.KeepOnScreen(Rect(1800, 1000, 400, 300), 1024, 768),
                "saved at 1920x1080, loaded at 1024x768");
            AssertRect(624, 100, 400, 300, WindowPlacement.KeepOnScreen(Rect(700, 100, 400, 300), 1024, 768), "one axis only");
        }

        [Test]
        public void AWindowLargerThanTheScreenKeepsItsSizeAndItsHeader()
        {
            AssertRect(0, 0, 1300, 900, WindowPlacement.KeepOnScreen(Rect(200, 100, 1300, 900), 1024, 768), "both axes");
            AssertRect(0, 468, 1300, 300, WindowPlacement.KeepOnScreen(Rect(-50, 600, 1300, 300), 1024, 768),
                "wider only: left edge, height kept on screen");
        }

        [Test]
        public void AnUnknownScreenOrPositionIsHandled()
        {
            AssertRect(5000, -20, 400, 300, WindowPlacement.KeepOnScreen(Rect(5000, -20, 400, 300), 0, 0), "screen not known yet");
            AssertRect(0, 0, 400, 300, WindowPlacement.KeepOnScreen(Rect(float.NaN, float.NegativeInfinity, 400, 300), 1024, 768),
                "a broken position");
        }

        // DRAG-01: a 400x300 window, its header 20 high, its buttons from x 340 (60 wide), 48 reachable.

        private static kOS.Safe.UserIO.ScreenRect Reachable(float x, float y)
        {
            return WindowPlacement.KeepReachable(Rect(x, y, 400, 300), 340, 20, 48, 1024, 768);
        }

        [Test]
        public void AMovedWindowMayLeaveTheScreenByItsSidesAndBottom()
        {
            AssertRect(-200, 100, 400, 300, Reachable(-200, 100), "half off the left");
            AssertRect(900, 100, 400, 300, Reachable(900, 100), "most of it off the right");
            AssertRect(100, 700, 400, 300, Reachable(100, 700), "below the bottom, its header on screen");
            AssertRect(50, 60, 400, 300, Reachable(50, 60), "on screen: not moved");
        }

        [Test]
        public void AMovedWindowKeepsEnoughOfItsHeaderToMoveItBack()
        {
            AssertRect(-292, 100, 400, 300, Reachable(-1000, 100), "off the left: the buttons and 48 pixels left of them");
            AssertRect(976, 100, 400, 300, Reachable(2000, 100), "off the right: 48 pixels of the title's end");
            AssertRect(100, 748, 400, 300, Reachable(100, 1000), "off the bottom: the whole header");
            AssertRect(100, 0, 400, 300, Reachable(100, -5), "never above the top");
        }

        [Test]
        public void ANarrowWindowCannotLeaveByTheLeft()
        {
            AssertRect(0, 100, 90, 300, WindowPlacement.KeepReachable(Rect(-50, 100, 90, 300), 30, 20, 48, 1024, 768),
                "buttons and reachable part wider than the window");
        }

        [Test]
        public void KeepReachableHandlesAnUnknownScreenOrPosition()
        {
            AssertRect(-5000, -20, 400, 300, WindowPlacement.KeepReachable(Rect(-5000, -20, 400, 300), 340, 20, 48, 0, 0),
                "screen not known yet");
            AssertRect(0, 0, 400, 300, WindowPlacement.KeepReachable(Rect(float.NaN, float.PositiveInfinity, 400, 300), 340, 20, 48, 1024, 768),
                "a broken position");
        }
    }

    /// <summary>DRAG-01: when a window is kept wholly on screen and when only by its header.</summary>
    [TestFixture]
    public class WindowKeeperTest
    {
        private static kOS.Safe.UserIO.ScreenRect Rect(float x, float y, float width, float height)
        {
            return new kOS.Safe.UserIO.ScreenRect(x, y, width, height);
        }

        private static kOS.Safe.UserIO.ScreenRect Keep(WindowKeeper keeper, kOS.Safe.UserIO.ScreenRect window,
            float screenWidth = 1024, float screenHeight = 768, float scale = 1)
        {
            return keeper.Keep(window, window.Width - 60, 20, 48, screenWidth, screenHeight, scale);
        }

        private static WindowKeeper Drawn(kOS.Safe.UserIO.ScreenRect window)
        {
            var keeper = new WindowKeeper();
            Keep(keeper, window);
            return keeper;
        }

        [Test]
        public void TheFirstDrawFitsTheWindowWholly()
        {
            Assert.AreEqual(-200f, Keep(Drawn(Rect(50, 60, 400, 300)), Rect(-200, 60, 400, 300)).X, "then moved: partly off");
            Assert.AreEqual(0f, Keep(new WindowKeeper(), Rect(-200, 60, 400, 300)).X, "restored partly off");
        }

        [Test]
        public void AMovedWindowStaysPartlyOffAtLaterDraws()
        {
            WindowKeeper keeper = Drawn(Rect(50, 60, 400, 300));
            Keep(keeper, Rect(-200, 600, 400, 300));
            kOS.Safe.UserIO.ScreenRect again = Keep(keeper, Rect(-200, 600, 400, 300));
            Assert.AreEqual(-200f, again.X);
            Assert.AreEqual(600f, again.Y);
        }

        [Test]
        public void OpeningOrRestoringFitsItWholly()
        {
            WindowKeeper keeper = Drawn(Rect(50, 60, 400, 300));
            Keep(keeper, Rect(-200, 600, 400, 300));
            keeper.FitWhollyNext();
            kOS.Safe.UserIO.ScreenRect opened = Keep(keeper, Rect(-200, 600, 400, 300));
            Assert.AreEqual(0f, opened.X);
            Assert.AreEqual(468f, opened.Y);
            Assert.AreEqual(-100f, Keep(keeper, Rect(-100, 468, 400, 300)).X, "moved off again after");
        }

        [Test]
        public void AScreenOrScaleChangeFitsItWholly()
        {
            WindowKeeper keeper = Drawn(Rect(50, 60, 400, 300));
            Keep(keeper, Rect(-200, 60, 400, 300));
            Assert.AreEqual(0f, Keep(keeper, Rect(-200, 60, 400, 300), 1920, 1080).X, "resolution");
            Keep(keeper, Rect(-200, 60, 400, 300), 1920, 1080);
            Assert.AreEqual(0f, Keep(keeper, Rect(-200, 60, 400, 300), 1920, 1080, 1.3f).X, "UI scale");
        }

        [Test]
        public void ASizeChangeFitsItWhollyUnlessTheUserDraggedAnEdge()
        {
            WindowKeeper keeper = Drawn(Rect(50, 60, 400, 300));
            Keep(keeper, Rect(-200, 60, 400, 300));
            Assert.AreEqual(0f, Keep(keeper, Rect(-200, 60, 480, 300)).X, "character size or script");

            Keep(keeper, Rect(-200, 60, 480, 300));
            keeper.SizedByUser(Rect(-200, 60, 560, 300));
            Assert.AreEqual(-200f, Keep(keeper, Rect(-200, 60, 560, 300)).X, "the user's edge drag");
        }
    }
}
