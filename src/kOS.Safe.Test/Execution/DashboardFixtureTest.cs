using NUnit.Framework;
using System.Text;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// A character-grid dashboard (<c>fixtures/dashboard/hud.ks</c>) on the real screen
    /// model, checked against full-screen snapshots recorded in
    /// <c>fixtures/expected/dashboard.out</c>: PRINT AT, overwrites, wrapping, plain PRINT
    /// scrolling followed by a redraw, CLEARSCREEN, and resizes from the window/telnet side
    /// and from the script.
    /// <br/>
    /// Snapshots show each row between <c>|</c> marks and the cursor the window draws.
    /// Keys go to the script's input queue, as the window and telnet send them while a
    /// program runs.
    /// </summary>
    [TestFixture]
    public class DashboardFixtureTest
    {
        [Test]
        public void DashboardMatchesRecordedSnapshots()
        {
            var record = new StringBuilder();
            using (var host = new ScriptHost(8, 24))
            {
                host.Boot();
                host.Type("runpath(\"0:/fixtures/dashboard/hud.ks\").\n");
                host.RunUntil(() => host.Snapshot().Contains("f0 24x8"));
                host.RunUntilBlocked();
                Step(record, host, "first frame");

                foreach (string key in new[] { "n", "n", "s", "w" })
                {
                    host.PressKeys(key);
                    host.RunUntilBlocked();
                    Step(record, host, "key " + key);
                }

                host.Screen.SetSize(10, 30);
                host.RunUntilBlocked();
                Step(record, host, "window resized to 10x30");

                host.Screen.SetSize(5, 16);
                host.RunUntilBlocked();
                Step(record, host, "window resized to 5x16");

                host.Screen.SetSize(8, 24);
                host.RunUntilBlocked();
                Step(record, host, "window resized to 8x24");

                foreach (string key in new[] { "r", "p", "p", "p", "n", "c", "q" })
                {
                    host.PressKeys(key);
                    if (key == "q")
                        host.RunUntilPrompt();
                    else
                        host.RunUntilBlocked();
                    Step(record, host, "key " + key);
                }
                host.AssertNoErrors();
            }
            ScriptFixtureTest.AssertRecorded("dashboard", false, record.ToString().TrimEnd('\n'));
        }

        private static void Step(StringBuilder record, ScriptHost host, string label)
        {
            record.Append("== ").Append(label).Append('\n').Append(host.Snapshot()).Append("\n\n");
        }
    }
}
