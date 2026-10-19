using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// P2-2 is meant to change no behavior: the same seeded sequences of output, resizes,
    /// client sizes, attaches, detaches, clears, title changes, repaint requests, screen
    /// modes, beeps and refreshes, run on two identical screens, one with
    /// <see cref="TerminalSession{TViewer}"/> and one with the code it replaced
    /// (<see cref="PreSessionTermWindowFanOut"/>), send every client the same output.
    /// </summary>
    [TestFixture]
    public class TerminalSessionEquivalenceTest
    {
        private sealed class Pair
        {
            public RecordingViewer Old;
            public RecordingViewer New;
        }

        [Test]
        public void SeededSequencesSendTheSameOutputAsBefore()
        {
            for (int seed = 3301; seed <= 3340; ++seed)
                RunSeed(seed);
        }

        private static void RunSeed(int seed)
        {
            var random = new Random(seed);
            var start = new DateTime(2026, 10, 19, 12, 0, 0);
            DateTime now = start;

            var oldScreen = new ScreenBuffer();
            var newScreen = new ScreenBuffer();
            var oldWindow = new PreSessionTermWindowFanOut(oldScreen, start);
            var session = new TerminalSession<RecordingViewer>(start) { Screen = newScreen };
            newScreen.AddResizeNotifier(sb => { session.ScreenResized(); return 0; });

            var pairs = new List<Pair>();
            var detached = new List<Pair>();
            string[] titles = { "Ship CPU: CX-4181 ()", "Ship CPU: CX-4181 (main)", "Other: KAL9000 ()" };
            Action<Action<ScreenBuffer>> onBoth = action => { action(oldScreen); action(newScreen); };

            oldWindow.ChangeTitle(titles[0]);
            session.ChangeTitle(titles[0]);

            for (int step = 0; step < 600; ++step)
            {
                string what;
                int op = step == 0 ? 99 : random.Next(100); // the window refreshes before a client can attach
                if (op < 25)
                {
                    string text = RandomText(random);
                    bool newLine = random.Next(3) > 0;
                    what = "print";
                    onBoth(screen => screen.Print(text, newLine));
                }
                else if (op < 33)
                {
                    string text = RandomText(random);
                    int row = random.Next(newScreen.RowCount);
                    int column = random.Next(newScreen.ColumnCount);
                    what = "print at";
                    onBoth(screen => screen.PrintAt(text, row, column));
                }
                else if (op < 36)
                {
                    int delta = random.Next(-8, 9);
                    what = "scroll " + delta;
                    onBoth(screen => screen.ScrollVertical(delta));
                }
                else if (op < 39)
                {
                    int rows = random.Next(1, 40);
                    int columns = random.Next(1, 120);
                    what = "resize " + columns + "x" + rows;
                    onBoth(screen => screen.SetSize(rows, columns));
                }
                else if (op < 41)
                {
                    what = "font change";
                    oldWindow.NotifyOfScreenResize(oldScreen);
                    session.ScreenResized();
                }
                else if (op < 44 && pairs.Count > 0)
                {
                    Pair pair = pairs[random.Next(pairs.Count)];
                    int width = random.Next(10, 120);
                    int height = random.Next(3, 40);
                    what = "client " + pair.New + " reports " + width + "x" + height;
                    pair.Old.UserResize(width, height);
                    pair.New.UserResize(width, height);
                    onBoth(screen => screen.SetSize(height, width));
                    oldWindow.RepaintClient(pair.Old); // the repaint request that comes with a NAWS
                    session.RepaintViewer(pair.New);
                }
                else if (op < 46)
                {
                    what = "beep";
                    onBoth(screen => screen.Print("" + (char)7, false));
                }
                else if (op < 48)
                {
                    bool reverse = random.Next(2) == 0;
                    bool visual = random.Next(2) == 0;
                    what = "modes";
                    onBoth(screen => { screen.ReverseScreen = reverse; screen.VisualBeep = visual; });
                }
                else if (op < 52 && pairs.Count < 4)
                {
                    int width = random.Next(10, 120);
                    int height = random.Next(3, 40);
                    string name = "v" + step;
                    var pair = new Pair
                    {
                        Old = new RecordingViewer(width, height) { Name = name },
                        New = new RecordingViewer(width, height) { Name = name }
                    };
                    pair.New.Model.ObeysResize = pair.Old.Model.ObeysResize = random.Next(4) > 0;
                    pairs.Add(pair);
                    what = "attach " + name + " " + width + "x" + height;
                    oldWindow.ConnectToProcessor(pair.Old);
                    session.AttachViewer(pair.New);
                }
                else if (op < 54 && pairs.Count > 0)
                {
                    Pair pair = pairs[random.Next(pairs.Count)];
                    pairs.Remove(pair);
                    detached.Add(pair);
                    what = "detach " + pair.New;
                    oldWindow.DetachTelnet(pair.Old);
                    session.DetachViewer(pair.New);
                }
                else if (op < 55)
                {
                    detached.AddRange(pairs);
                    pairs.Clear();
                    what = "detach all";
                    oldWindow.DetachAllTelnets();
                    session.DetachAllViewers();
                }
                else if (op < 58)
                {
                    what = "clear";
                    oldWindow.ClearScreen();
                    session.ClearScreen();
                }
                else if (op < 60)
                {
                    string title = titles[random.Next(titles.Length)];
                    what = "title " + title;
                    oldWindow.ChangeTitle(title);
                    session.ChangeTitle(title);
                }
                else if (op < 63 && pairs.Count > 0)
                {
                    Pair pair = pairs[random.Next(pairs.Count)];
                    what = "repaint request from " + pair.New;
                    oldWindow.RepaintClient(pair.Old);
                    session.RepaintViewer(pair.New);
                }
                else
                {
                    now += TimeSpan.FromMilliseconds(random.Next(0, 120));
                    what = "update at " + (now - start).TotalMilliseconds + " ms";
                    bool oldRefreshed = oldWindow.Update(now);
                    bool newRefreshed = session.Update(now);
                    Assert.AreEqual(oldRefreshed, newRefreshed, Where(seed, step, what) + "refreshed");
                    if (newRefreshed)
                        onBoth(screen => screen.BeepsPending = 0); // TermWindow hands them to the GUI
                }

                string where = Where(seed, step, what);
                Assert.AreEqual(oldWindow.NumTelnets(), session.ViewerCount, where + "viewer count");
                Assert.AreEqual(oldWindow.TitleText, session.Title, where + "title");
                Assert.AreEqual(Rows(oldWindow.MostRecentScreen), Rows(session.Snapshot), where + "snapshot");
                foreach (Pair pair in pairs.Concat(detached))
                {
                    if (pair.Old.Output != pair.New.Output) // described only when they differ: it is slow
                        Assert.AreEqual(Escape(pair.Old.Output), Escape(pair.New.Output), where + "output to " + pair.New);
                    Assert.AreEqual(pair.Old.DetachCount, pair.New.DetachCount, where + "detaches of " + pair.New);
                    Assert.AreEqual(pair.Old.ClientWidth, pair.New.ClientWidth, where + "width of " + pair.New);
                    Assert.AreEqual(pair.Old.ReverseScreen, pair.New.ReverseScreen, where + "reverse mode of " + pair.New);
                    Assert.AreEqual(pair.Old.VisualBeep, pair.New.VisualBeep, where + "beep mode of " + pair.New);
                }
            }
        }

        private static string Where(int seed, int step, string what)
        {
            return "seed " + seed + ", step " + step + " (" + what + "): ";
        }

        private static string Rows(IScreenSnapShot snapshot)
        {
            if (snapshot == null)
                return "(none)";
            var text = new System.Text.StringBuilder();
            text.AppendFormat("{0} rows, cursor {1},{2}, top {3}\n", snapshot.RowCount, snapshot.CursorColumn, snapshot.CursorRow, snapshot.TopRow);
            foreach (IScreenBufferLine line in snapshot.Buffer)
                text.Append(line.ToString()).Append('\n');
            return text.ToString();
        }

        private static string Escape(string output)
        {
            return string.Join("\n", RecordingViewer.Describe(output).ToArray());
        }

        private static string RandomText(Random random)
        {
            const string letters = "abcdefghijklmnopqrstuvwxyz0123456789 .:-";
            int length = random.Next(0, 90);
            var chars = new char[length];
            for (int i = 0; i < length; ++i)
                chars[i] = letters[random.Next(letters.Length)];
            return new string(chars);
        }
    }
}
