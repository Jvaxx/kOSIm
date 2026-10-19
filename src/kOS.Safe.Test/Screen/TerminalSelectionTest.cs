using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 3, decision O3: selecting cells in the GUI terminal (<see cref="TerminalSelection"/>),
    /// the rows it reads (<see cref="ScreenBuffer.GetLineAt"/>) and the copied text
    /// (<see cref="TerminalCopy.SelectionText"/>).
    /// </summary>
    [TestFixture]
    public class TerminalSelectionTest
    {
        private static ScreenBuffer ScreenWithLines(int rows, int columns, int lines)
        {
            var screen = new ScreenBuffer();
            screen.SetSize(rows, columns);
            screen.ClearScreen();
            for (int i = 0; i < lines; ++i)
                screen.Print("line " + i);
            return screen;
        }

        private static TerminalSelection Select(int fromRow, int fromColumn, int toRow, int toColumn)
        {
            var selection = new TerminalSelection();
            selection.Begin(fromRow, fromColumn);
            selection.DragTo(toRow, toColumn);
            selection.EndDrag();
            return selection;
        }

        [Test]
        public void AClickAloneSelectsNothing()
        {
            var selection = new TerminalSelection();
            Assert.IsTrue(selection.IsEmpty);
            selection.Begin(3, 4);
            Assert.IsTrue(selection.IsDragging);
            Assert.IsTrue(selection.IsEmpty);
            selection.EndDrag();
            selection.DragTo(5, 0);
            Assert.IsTrue(selection.IsEmpty, "a drag after the button is up does nothing");
        }

        [Test]
        public void RowsInReadingOrder()
        {
            TerminalSelection selection = Select(5, 7, 3, 2); // dragged up and left
            Assert.AreEqual(3, selection.StartRow);
            Assert.AreEqual(5, selection.EndRow);
            int from, to;
            Assert.IsTrue(selection.TryGetColumns(3, 40, out from, out to));
            Assert.AreEqual(new[] { 2, 40 }, new[] { from, to }, "the first row from the start");
            Assert.IsTrue(selection.TryGetColumns(4, 40, out from, out to));
            Assert.AreEqual(new[] { 0, 40 }, new[] { from, to }, "rows between, whole");
            Assert.IsTrue(selection.TryGetColumns(5, 40, out from, out to));
            Assert.AreEqual(new[] { 0, 7 }, new[] { from, to }, "the last row up to the end");
            Assert.IsFalse(selection.TryGetColumns(6, 40, out from, out to));
            Assert.IsFalse(Select(3, 4, 4, 0).TryGetColumns(4, 40, out from, out to), "nothing of a row ending at its start");
        }

        [Test]
        public void TheSelectedText()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 6);
            int top = screen.TopRow;
            Assert.AreEqual("ne 1\nline 2\nlin", TerminalCopy.SelectionText(screen, Select(top + 1, 2, top + 3, 3)));
            Assert.AreEqual("line 4", TerminalCopy.SelectionText(screen, Select(top + 4, 0, top + 4, 40)), "trailing blanks left out");
            Assert.AreEqual("", TerminalCopy.SelectionText(screen, Select(top + 1, 2, top + 1, 2)));
        }

        [Test]
        public void ASelectionStaysOnItsTextWhileOutputScrolls()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 30);
            int row = screen.TopRow + 2; // shows "line 23"
            TerminalSelection selection = Select(row, 0, row, 40);
            Assert.AreEqual("line 23", TerminalCopy.SelectionText(screen, selection));
            for (int i = 0; i < 15; ++i)
                screen.Print("more " + i);
            screen.ScrollVertical(-7);
            Assert.AreEqual("line 23", TerminalCopy.SelectionText(screen, selection));
        }

        [Test]
        public void DroppedRowsAreSkipped()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 20);
            int first = screen.TopRow - screen.ScrollbackRows; // the oldest row kept: "line 0"
            Assert.AreEqual("line 0", screen.GetLineAt(first).ToString().TrimEnd('\0', ' '));
            TerminalSelection selection = Select(first, 0, first + 1, 40);
            for (int i = 0; i < ScreenBuffer.MAX_SCROLLBACK; ++i)
                screen.Print("x");
            Assert.IsNull(screen.GetLineAt(first), "dropped");
            Assert.IsNull(screen.GetLineAt(first - 1));
            Assert.AreEqual("", TerminalCopy.SelectionText(screen, selection));
        }

        [Test]
        public void TheLineBeingTypedIsSelectableAsShown()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                host.Type("print \"typed\"");
                int row = host.Screen.TopRow + host.Screen.CursorRowShow;
                Assert.AreEqual("print \"typed\"", host.Screen.GetLineAt(row).ToString().Replace('\0', ' ').TrimEnd());
                Assert.AreEqual("\"typed\"", TerminalCopy.SelectionText(host.Screen, Select(row, 6, row, 40)));
            }
        }

        [Test]
        public void GetLineAtMatchesTheView()
        {
            using (var host = new ScriptHost(8, 30))
            {
                host.Boot();
                for (int i = 0; i < 20; ++i)
                    host.Screen.Print("row " + i);
                host.Type("abc");
                foreach (int scroll in new[] { 0, -3 }) // with the line being typed in view, then not
                {
                    host.Screen.ScrollVertical(scroll);
                    var view = host.Screen.GetBuffer();
                    for (int i = 0; i < view.Count; ++i)
                        Assert.AreEqual(view[i].ToString(), host.Screen.GetLineAt(host.Screen.TopRow + i).ToString(),
                            "scrolled " + scroll + ", view row " + i);
                }
                Assert.IsNull(host.Screen.GetLineAt(host.Screen.TopRow + 1000));
            }
        }
    }
}
