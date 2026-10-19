using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// The screen/view split (Phase 2, P2-4; decisions D1 and D2) on the real screen model and
    /// the real prompt: PageUp/PageDown move only the displayed view, every write goes to the
    /// screen (VIEW-01), output and typing bring the view back, and the scrollback keeps at most
    /// <see cref="ScreenBuffer.MAX_SCROLLBACK"/> lines (VIEW-02).
    /// </summary>
    [TestFixture]
    public class ScreenViewTest
    {
        private static string Row(IScreenBufferLine line)
        {
            return line.ToString().Replace('\0', ' ').TrimEnd();
        }

        private static List<string> View(IScreenBuffer screen)
        {
            return screen.GetBuffer().Select(Row).ToList();
        }

        /// <summary>
        /// Every line in the buffer, scrollback included, read by moving the view; the view is
        /// put back afterwards.
        /// </summary>
        private static List<string> AllLines(ScreenBuffer screen)
        {
            int offset = screen.ViewOffset;
            screen.ScrollVertical(-int.MaxValue / 2);
            var lines = new List<string>();
            int nextRow = screen.TopRow;
            while (true)
            {
                List<IScreenBufferLine> view = screen.GetBuffer();
                for (int i = nextRow - screen.TopRow; i < view.Count; ++i)
                    lines.Add(Row(view[i]));
                nextRow = screen.TopRow + view.Count;
                if (screen.ScrollVertical(screen.RowCount) == 0)
                    break;
            }
            screen.ScrollVertical(screen.ViewOffset - offset);
            Assert.AreEqual(offset, screen.ViewOffset, "view put back");
            return lines;
        }

        private static ScreenBuffer ScreenWithLines(int rows, int columns, int lines)
        {
            var screen = new ScreenBuffer();
            screen.SetSize(rows, columns);
            screen.ClearScreen();
            for (int i = 0; i < lines; ++i)
                screen.Print("line " + i);
            return screen;
        }

        // ---- At the prompt (the VIEW-01 case) ----

        private static void PageUp(ScriptHost host) { host.Screen.SpecialKey((char)UnicodeCommand.PAGEUPCURSOR); }
        private static void PageDown(ScriptHost host) { host.Screen.SpecialKey((char)UnicodeCommand.PAGEDOWNCURSOR); }

        [Test]
        public void ACommandTypedAfterPageUpIsWrittenBelowTheOutputNotIntoTheScrollback()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < 30; ++i)
                    host.Screen.Print("line " + i);
                List<string> before = AllLines(host.Screen);

                PageUp(host);
                Assert.AreEqual(10, host.Screen.ViewOffset);
                Assert.AreEqual("line 11", View(host.Screen)[0], "the view shows older output");

                host.Execute("print \"NEW OUTPUT\".");

                Assert.AreEqual(0, host.Screen.ViewOffset, "typing brought the view back");
                List<string> after = AllLines(host.Screen);
                // Upstream (VIEW-01): the command and its output replaced "line 20" and "line 21".
                CollectionAssert.AreEqual(before.Take(30), after.Take(30), "older output unchanged");
                Assert.AreEqual("print \"NEW OUTPUT\".", after[30]);
                Assert.AreEqual("NEW OUTPUT", after[31]);
                host.AssertNoErrors();
            }
        }

        [Test]
        public void PageUpAndPageDownMoveOnlyTheView()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < 30; ++i)
                    host.Screen.Print("line " + i);
                host.Type("abc");
                int cursorRow = host.Screen.AbsoluteCursorRow;
                int cursorColumn = host.Screen.CursorColumnShow;
                int top = host.Screen.TopRow;
                int shownRow = host.Screen.CursorRowShow;
                List<string> screenRows = View(host.Screen);

                PageUp(host);
                Assert.AreEqual(top - 10, host.Screen.TopRow, "the display scrolled by 10");
                Assert.AreEqual(cursorRow, host.Screen.AbsoluteCursorRow, "where output goes is unchanged");
                Assert.AreEqual(cursorColumn, host.Screen.CursorColumnShow);
                Assert.AreEqual(shownRow + 10, host.Screen.CursorRowShow, "the cursor is shown 10 rows lower");
                CollectionAssert.DoesNotContain(View(host.Screen), "abc", "the input line stays on its line, below the view");

                PageDown(host);
                Assert.AreEqual(0, host.Screen.ViewOffset);
                CollectionAssert.AreEqual(screenRows, View(host.Screen));
            }
        }

        [Test]
        public void TypingAKeyAtThePromptBringsTheViewBack()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < 30; ++i)
                    host.Screen.Print("line " + i);

                PageUp(host);
                PageUp(host);
                Assert.AreEqual(20, host.Screen.ViewOffset, "paging keys keep the view");
                host.Type("x");
                Assert.AreEqual(0, host.Screen.ViewOffset);

                PageUp(host);
                host.Screen.SpecialKey((char)UnicodeCommand.LEFTCURSORONE);
                Assert.AreEqual(0, host.Screen.ViewOffset, "moving the input cursor shows it");

                PageUp(host);
                host.Screen.SpecialKey((char)UnicodeCommand.DELETERIGHT);
                Assert.AreEqual(0, host.Screen.ViewOffset, "editing the input line shows it");

                host.Execute("print 1.");
                PageUp(host);
                host.Screen.SpecialKey((char)UnicodeCommand.UPCURSORONE); // history recalls "print 1."
                Assert.AreEqual(0, host.Screen.ViewOffset, "recalling history shows the input line");
            }
        }

        [Test]
        public void PrintAtWhileScrolledWritesToTheScreen()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < 30; ++i)
                    host.Screen.Print("line " + i);
                List<string> scrollback = AllLines(host.Screen).Take(host.Screen.ScrollbackRows).ToList();
                PageUp(host);

                host.Screen.PrintAt("AT", 0, 0); // what PRINT ... AT (0, 0) does

                Assert.AreEqual(0, host.Screen.ViewOffset, "output brought the view back");
                Assert.AreEqual("ATne " + (30 - 10 + 1), View(host.Screen)[0], "row 0 is the screen's top row");
                CollectionAssert.AreEqual(scrollback, AllLines(host.Screen).Take(scrollback.Count), "scrollback unchanged");
            }
        }

        // ---- The model ----

        [Test]
        public void TheViewStopsAtTheTopOfTheScrollback()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 25);
            Assert.AreEqual(16, screen.ScrollbackRows);

            Assert.AreEqual(-16, screen.ScrollVertical(-100));
            Assert.AreEqual(16, screen.ViewOffset);
            Assert.AreEqual("line 0", View(screen)[0]);
            Assert.AreEqual(0, screen.ScrollVertical(-1));
            Assert.AreEqual(16, screen.ScrollVertical(100), "and at the screen");
            Assert.AreEqual(0, screen.ViewOffset);
        }

        [Test]
        public void TheCursorIsShownWhereItIsInTheViewAndHiddenOutsideIt()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 25);
            screen.Print("x", false);
            Assert.AreEqual(9, screen.CursorRowShow);

            screen.ScrollVertical(-3);
            Assert.AreEqual(12, screen.CursorRowShow, "below the view");
            char ch;
            Assert.IsFalse(new ScreenSnapShot(screen).TryGetCursorCharacter(screen.RowCount, screen.ColumnCount, out ch),
                "the GUI draws no cursor outside the view");
            Assert.AreEqual(9, ScreenSnapShot.TeleportRow(12, 10), "a telnet cursor waits at the bottom edge");
        }

        [Test]
        public void ResizingWhileScrolledKeepsTheViewAndWritesStillGoToTheScreen()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 40);
            screen.ScrollVertical(-12);
            var notified = new List<int>();
            screen.AddResizeNotifier(sb => { notified.Add(sb.RowCount); return 0; });

            screen.SetSize(6, 30);

            CollectionAssert.AreEqual(new[] { 6 }, notified, "watchers see the screen's size");
            Assert.AreEqual(12, screen.ViewOffset, "same view, as far as the new size allows");
            screen.Print("after");
            Assert.AreEqual(0, screen.ViewOffset);
            // As before P2-4, a shorter screen keeps its top row, and the cursor moves up onto
            // older output, which a write only partly covers (VIEW-04, not changed):
            StringAssert.StartsWith("after", View(screen)[screen.CursorRowShow - 1]);
        }

        [Test]
        public void ClearScreenWhileScrolledClearsTheScrollbackAndTheView()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 40);
            screen.ScrollVertical(-12);

            screen.ClearScreen();

            Assert.AreEqual(0, screen.ViewOffset);
            Assert.AreEqual(0, screen.ScrollbackRows);
            Assert.IsTrue(View(screen).All(row => row.Length == 0));
        }

        [Test]
        public void ANonFixedOverlayScrollsWithTheViewAndAFixedOneStays()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 30);
            var input = new SubBuffer { Enabled = true, Fixed = false, PositionRow = screen.AbsoluteCursorRow - 5 };
            input.SetSize(1, 40);
            input.Buffer[0].ArrayCopyFrom("INPUT".ToCharArray(), 0, 0);
            var bar = new SubBuffer { Enabled = true, Fixed = true, PositionRow = 9 };
            bar.SetSize(1, 40);
            bar.Buffer[0].ArrayCopyFrom("BAR".ToCharArray(), 0, 0);
            screen.AddSubBuffer(input);
            screen.AddSubBuffer(bar);
            int inputRow = screen.CursorRowShow - 5;

            screen.ScrollVertical(-3);

            List<string> view = View(screen);
            Assert.AreEqual("INPUT", view[inputRow + 3], string.Join("|", view));
            Assert.AreEqual("BAR", view[9]);
        }

        // ---- Bounded scrollback (D2) ----

        [Test]
        public void TheScrollbackKeepsTheNewestLinesUpToTheLimit()
        {
            int lines = ScreenBuffer.MAX_SCROLLBACK + 700;
            ScreenBuffer screen = ScreenWithLines(10, 40, lines);

            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, screen.ScrollbackRows);
            List<string> all = AllLines(screen);
            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK + 10, all.Count, "scrollback + screen");
            // The cursor is on the empty last screen row; the newest output just above it:
            Assert.AreEqual("line " + (lines - 1), all[all.Count - 2]);
            Assert.AreEqual("line " + (lines - 1 - (ScreenBuffer.MAX_SCROLLBACK + 8)), all[0], "oldest kept");
            Assert.AreEqual(lines - 9, screen.TopRow, "TopRow still counts every line scrolled");
        }

        [Test]
        public void TheScreenIsNeverTrimmed()
        {
            ScreenBuffer screen = ScreenWithLines(ScreenBuffer.MAX_ROWS, 20, ScreenBuffer.MAX_SCROLLBACK + 500);
            screen.PrintAt("top", 0, 0);

            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, screen.ScrollbackRows);
            Assert.AreEqual(ScreenBuffer.MAX_ROWS, screen.GetBuffer().Count);
            Assert.AreEqual("top", View(screen)[0].Substring(0, 3));
        }

        [Test]
        public void PrintAtRestoresTheCursorToTheSameLineAcrossDroppedScrollback()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < ScreenBuffer.MAX_SCROLLBACK + 20; ++i)
                    host.Screen.Print("line " + i);
                host.Screen.Print("mark", false);
                int row = host.Screen.CursorRowShow;

                // Row 10 of a 10-row screen scrolls the screen by one, which drops a line:
                host.Screen.PrintAt("below", 10, 0);

                Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, host.Screen.ScrollbackRows);
                Assert.AreEqual(row - 1, host.Screen.CursorRowShow, "back on the same line, now one row higher");
                host.Screen.SetInputLock(true); // the prompt's input line covers the cursor's line
                Assert.AreEqual("mark", View(host.Screen)[row - 1]);
            }
        }

        [Test]
        public void ThePromptKeepsWorkingAtTheLimitWithAWrappedCommand()
        {
            using (var host = new ScriptHost(5, 30))
            {
                host.Boot();
                for (int i = 0; i < ScreenBuffer.MAX_SCROLLBACK / 2 + 50; ++i)
                    host.Execute("print " + i + ".");
                Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, host.Screen.ScrollbackRows);

                string text = new string('w', 70); // the command wraps over three rows
                host.Execute("print \"" + text + "\".");

                List<string> lines = AllLines(host.Screen);
                int end = lines.FindLastIndex(line => line.Length > 0);
                Assert.AreEqual(text.Substring(60), lines[end], "the output's last row");
                Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, host.Screen.ScrollbackRows);
                host.AssertNoErrors();
            }
        }

        // ---- Seeded ----

        [Test]
        public void SeededSequencesNeverChangeTheScrollbackAndViewMovesNeverChangeTheScreen()
        {
            foreach (int seed in new[] { 4401, 4402, 4403, 4404 })
                RunSeed(seed);
        }

        private static void RunSeed(int seed)
        {
            var random = new Random(seed);
            var screen = new ScreenBuffer();
            screen.SetSize(8, 20);
            screen.ClearScreen();
            int printed = 0;
            for (int step = 0; step < 3000; ++step)
            {
                string context = "seed " + seed + ", step " + step;
                int kind = random.Next(100);
                if (kind < 25)
                {
                    // View moves change nothing but the view:
                    List<string> lines = AllLines(screen);
                    int absoluteCursor = screen.AbsoluteCursorRow, column = screen.CursorColumnShow;
                    screen.ScrollVertical(random.Next(-25, 26));
                    CollectionAssert.AreEqual(lines, AllLines(screen), context + ": content");
                    Assert.AreEqual(absoluteCursor, screen.AbsoluteCursorRow, context + ": cursor row");
                    Assert.AreEqual(column, screen.CursorColumnShow, context + ": cursor column");
                    Assert.LessOrEqual(screen.ViewOffset, screen.ScrollbackRows, context + ": view within the buffer");
                    continue;
                }

                // Writes never change the scrollback, apart from dropping its oldest lines:
                int topBefore = screen.TopRow + screen.ViewOffset; // the screen's top, in TopRow's count
                List<string> before = AllLines(screen);
                int scrollbackBefore = screen.ScrollbackRows;
                if (kind < 75)
                {
                    string text = "p" + printed++ + new string('.', random.Next(0, 45));
                    screen.Print(text, random.Next(4) != 0);
                }
                else if (kind < 90)
                    screen.PrintAt("@" + step, random.Next(-2, 12), random.Next(-2, 24));
                else if (kind < 97)
                    screen.SetSize(random.Next(3, 12), random.Next(8, 30));
                else
                {
                    screen.ClearScreen();
                    continue;
                }
                if (kind < 90)
                    Assert.AreEqual(0, screen.ViewOffset, context + ": output shows the screen");
                int topAfter = screen.TopRow + screen.ViewOffset;
                int dropped = (topAfter - topBefore) - (screen.ScrollbackRows - scrollbackBefore);
                Assert.GreaterOrEqual(dropped, 0, context);
                List<string> after = AllLines(screen);
                for (int i = dropped; i < scrollbackBefore; ++i)
                {
                    string old = before[i];
                    string now = after[i - dropped];
                    // A narrower screen cuts every line on the right; nothing else may change.
                    string expected = old.Length > screen.ColumnCount ? old.Substring(0, screen.ColumnCount).TrimEnd() : old;
                    Assert.AreEqual(expected, now, context + ": scrollback line " + i);
                }
                Assert.LessOrEqual(screen.ScrollbackRows, ScreenBuffer.MAX_SCROLLBACK, context);
            }
        }
    }
}
