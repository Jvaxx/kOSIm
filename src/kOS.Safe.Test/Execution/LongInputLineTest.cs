using System.Linq;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// PROMPT-01: when typing wrapped the input line past the bottom row,
    /// <c>TextEditor.KeepCursorInBounds</c> moved the line up before adding the rows to
    /// scroll into, so the scroll stopped one row short. The line then sat one row too
    /// high: a command wrapped at the bottom of the screen overwrote the last output row,
    /// and a line taller than the screen typed from the top row was anchored above the
    /// buffer, so Enter threw <c>ArgumentOutOfRangeException</c> (a pasted long line
    /// broke the prompt).
    /// </summary>
    [TestFixture]
    public class LongInputLineTest
    {
        [Test]
        public void CommandWrappedAtTheBottomKeepsTheLastOutputRow()
        {
            using (var host = new ScriptHost())
            {
                host.Boot();
                host.Execute("from {local i is 0.} until i = 40 step {set i to i + 1.} do { print \"line \" + i. }");
                string command = "print \"" + new string('b', 60) + "\":length.";
                host.Execute(command);
                host.AssertNoErrors();

                string[] rows = host.Transcript().Split('\n');
                int echo = (command.Length + host.Screen.ColumnCount - 1) / host.Screen.ColumnCount;
                Assert.AreEqual("line 39", rows[rows.Length - echo - 2], "the command overwrote the last output row");
                Assert.AreEqual(command, string.Concat(rows.Skip(rows.Length - echo - 1).Take(echo).ToArray()));
                Assert.AreEqual("60", rows.Last());
            }
        }

        [TestCase(1800)] // exactly the default 50x36 screen with the command around it
        [TestCase(2000)]
        [TestCase(5000)]
        public void LineTallerThanTheScreenRuns(int length)
        {
            using (var host = new ScriptHost())
            {
                host.Boot();
                host.Execute("print \"" + new string('a', length) + "\":length.");
                host.AssertNoErrors();
                Assert.AreEqual(length.ToString(), host.Transcript().Split('\n').Last());
                Assert.AreEqual("ok", host.Output("print \"ok\"."), "the prompt still works");
            }
        }

        [Test]
        public void SeveralWrappedCommandsInARowKeepAllOutput()
        {
            using (var host = new ScriptHost(rows: 5))
            {
                host.Boot();
                for (int i = 0; i < 6; ++i)
                    host.Execute("print " + i + " + 0 * " + new string('1', 60) + ".");
                host.AssertNoErrors();
                string[] printed = host.Transcript().Split('\n').Where(row => row.Length == 1).ToArray();
                CollectionAssert.AreEqual(new[] { "0", "1", "2", "3", "4", "5" }, printed);
            }
        }
    }
}
