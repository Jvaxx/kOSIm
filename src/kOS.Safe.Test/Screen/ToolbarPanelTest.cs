using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>Phase 4, step 4: the toolbar panel's layout, scrolling and parts.</summary>
    [TestFixture]
    public class ToolbarPanelLayoutTest
    {
        private static readonly float[] scales = { 0.5f, 0.8f, 1f, 1.3f, 1.5f, 2f, 3f };

        private static bool Overlap(ScreenRect a, ScreenRect b)
        {
            return a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        }

        private static void AssertWithin(ScreenRect inner, ScreenRect outer, string what)
        {
            Assert.GreaterOrEqual(inner.X, outer.X, what);
            Assert.GreaterOrEqual(inner.Y, outer.Y, what);
            Assert.LessOrEqual(inner.X + inner.Width, outer.X + outer.Width + 1e-3, what);
            Assert.LessOrEqual(inner.Y + inner.Height, outer.Y + outer.Height + 1e-3, what);
        }

        [Test]
        public void ThePartsTileTheWindow()
        {
            foreach (float scale in scales)
                foreach (bool description in new[] { false, true })
                    foreach (float content in new[] { 0f, 50f, 300f, 2000f })
                    {
                        var layout = new ToolbarPanelLayout(scale);
                        string what = string.Format("scale {0}, description {1}, content {2}", scale, description, content);
                        float height = layout.WindowHeight(content, description, 700);
                        var window = new ScreenRect(0, 0, layout.Width, height);
                        ScreenRect header = layout.Header(), tabs = new ScreenRect(0, layout.HeaderHeight, layout.Width, layout.TabHeight);
                        ScreenRect body = layout.Content(height, description), footer = layout.Footer(height);
                        foreach (ScreenRect part in new[] { header, tabs, body, footer, layout.SuppressButton(height), layout.TelnetLine(height) })
                        {
                            Assert.GreaterOrEqual(part.Width, 0, what);
                            Assert.GreaterOrEqual(part.Height, 0, what);
                            AssertWithin(part, window, what);
                        }
                        Assert.IsFalse(Overlap(header, body) || Overlap(tabs, body) || Overlap(body, footer), what);
                        AssertWithin(layout.SuppressButton(height), footer, what);
                        AssertWithin(layout.TelnetLine(height), footer, what);
                        Assert.IsFalse(Overlap(layout.SuppressButton(height), layout.TelnetLine(height)), what);
                        Assert.GreaterOrEqual(body.Height, layout.MinimumContentHeight, what);
                        if (description)
                        {
                            ScreenRect text = layout.Description(height);
                            AssertWithin(text, window, what);
                            Assert.IsFalse(Overlap(text, body) || Overlap(text, footer), what);
                        }
                        Assert.AreEqual(height, footer.Y + footer.Height, 1e-3, what);
                    }
        }

        [Test]
        public void TheTabsShareTheWidth()
        {
            foreach (float scale in scales)
            {
                var layout = new ToolbarPanelLayout(scale);
                ScreenRect first = layout.Tab(0, 2), second = layout.Tab(1, 2);
                Assert.AreEqual(0, first.X);
                Assert.AreEqual(first.X + first.Width, second.X, "no gap");
                Assert.AreEqual(layout.Width, second.X + second.Width, "to the right edge");
                Assert.AreEqual(layout.HeaderHeight, first.Y);
                Assert.AreEqual(0, layout.Tab(2, 2).Width, "no such tab");
            }
        }

        [Test]
        public void TheHeightFollowsTheContentUpToTheRoom()
        {
            var layout = new ToolbarPanelLayout(1f);
            int fixedHeight = layout.FixedHeight(false);
            Assert.AreEqual(fixedHeight + 200, layout.WindowHeight(200, false, 700), "fits");
            Assert.AreEqual(700, layout.WindowHeight(2000, false, 700), "capped by the room");
            Assert.AreEqual(fixedHeight + layout.MinimumContentHeight, layout.WindowHeight(10, false, 700), "at least the minimum content");
            Assert.AreEqual(fixedHeight + layout.MinimumContentHeight, layout.WindowHeight(2000, false, 50), "a tiny room keeps the minimum");
            Assert.AreEqual(fixedHeight + 2000, layout.WindowHeight(2000, false, 0), "no room known: no cap");
            Assert.AreEqual(layout.FixedHeight(false) + layout.DescriptionHeight, layout.FixedHeight(true));
            Assert.AreEqual(2 * layout.Width, new ToolbarPanelLayout(2f).Width, "the width follows the scale");
            Assert.AreEqual(ToolbarPanelLayout.BASE_WIDTH, new ToolbarPanelLayout(float.NaN).Width, "NaN is scale 1");
        }

        [Test]
        public void ScrollingKeepsTheContentInView()
        {
            var layout = new ToolbarPanelLayout(1f);
            Assert.AreEqual(0, ToolbarPanelLayout.ClampScroll(-5, 500, 200));
            Assert.AreEqual(300, ToolbarPanelLayout.ClampScroll(1000, 500, 200));
            Assert.AreEqual(0, ToolbarPanelLayout.ClampScroll(50, 100, 200), "content that fits does not scroll");
            Assert.AreEqual(0, ToolbarPanelLayout.ClampScroll(float.NaN, 500, 200));

            var track = new ScreenRect(330, 50, 6, 200);
            ScreenRect top = layout.ScrollThumb(track, 500, 200, 0), bottom = layout.ScrollThumb(track, 500, 200, 300);
            Assert.AreEqual(track.Y, top.Y);
            Assert.AreEqual(track.Y + track.Height, bottom.Y + bottom.Height, 1e-3);
            Assert.AreEqual(80, top.Height, "the share in view");
            Assert.AreEqual(layout.MinimumThumb, layout.ScrollThumb(track, 100000, 200, 0).Height, "at least the minimum");
            Assert.AreEqual(track.Height, layout.ScrollThumb(track, 100, 200, 0).Height, "content that fits: the whole track");
            foreach (float scroll in new[] { 0f, 60f, 150f, 300f })
                Assert.AreEqual(scroll, layout.ScrollFor(layout.ScrollThumb(track, 500, 200, scroll).Y, track, 500, 200), 2.5,
                    "the thumb's place gives the scroll back: " + scroll);
            Assert.AreEqual(300, layout.ScrollFor(10000, track, 500, 200), "dragged past the end");
        }

        [Test]
        public void TheRowsMakeRoomForTheScrollBar()
        {
            var layout = new ToolbarPanelLayout(1f);
            Assert.AreEqual(layout.Width - 2 * layout.Padding, layout.RowWidth(false));
            Assert.AreEqual(layout.RowWidth(false) - layout.ScrollBarWidth - layout.Padding, layout.RowWidth(true));
            ScreenRect content = layout.Content(400, false), track = layout.ScrollTrack(content);
            Assert.AreEqual(content.X + content.Width, track.X + track.Width);
            Assert.Greater(track.X, content.X + layout.RowWidth(true), "the rows end before the bar");
        }
    }

    [TestFixture]
    public class ToolbarCpuListTest
    {
        private static ToolbarCpuEntry Cpu(object vessel, string tag = null, string state = "Ready")
        {
            return new ToolbarCpuEntry
            {
                Vessel = vessel, VesselName = vessel == null ? null : vessel.ToString(),
                PartTitle = "CX-4181 Scriptable Control System", Tag = tag,
                HasStatus = state != null, Kind = StatusKind.Ready, StateText = state
            };
        }

        [Test]
        public void EachVesselGetsAHeading()
        {
            var layout = new ToolbarPanelLayout(1f);
            var entries = new List<ToolbarCpuEntry> { Cpu("ship"), Cpu("ship", "second"), Cpu("probe"), Cpu("ship") };
            List<ToolbarCpuRow> rows = ToolbarCpuList.Rows(entries, layout);
            CollectionAssert.AreEqual(new[]
            {
                ToolbarCpuRowKind.Vessel, ToolbarCpuRowKind.Cpu, ToolbarCpuRowKind.Cpu,
                ToolbarCpuRowKind.Vessel, ToolbarCpuRowKind.Cpu,
                ToolbarCpuRowKind.Vessel, ToolbarCpuRowKind.Cpu
            }, rows.Select(row => row.Kind).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 2, 3, 3 }, rows.Select(row => row.Entry).ToArray());
            float y = 0;
            foreach (ToolbarCpuRow row in rows)
            {
                Assert.AreEqual(y, row.Y, "rows follow each other");
                Assert.AreEqual(row.Kind == ToolbarCpuRowKind.Vessel ? layout.VesselRowHeight : layout.CpuRowHeight, row.Height);
                y += row.Height;
            }
            Assert.AreEqual(y, ToolbarCpuList.Height(rows, layout));
        }

        [Test]
        public void ACpuWithoutAVesselHasNoHeading()
        {
            var layout = new ToolbarPanelLayout(1f);
            List<ToolbarCpuRow> rows = ToolbarCpuList.Rows(new List<ToolbarCpuEntry> { Cpu(null), Cpu("ship") }, layout);
            CollectionAssert.AreEqual(new[] { ToolbarCpuRowKind.Cpu, ToolbarCpuRowKind.Vessel, ToolbarCpuRowKind.Cpu },
                rows.Select(row => row.Kind).ToArray());
        }

        [Test]
        public void NoCpus()
        {
            var layout = new ToolbarPanelLayout(1f);
            List<ToolbarCpuRow> rows = ToolbarCpuList.Rows(new List<ToolbarCpuEntry>(), layout);
            Assert.AreEqual(0, rows.Count);
            Assert.AreEqual(layout.SettingRowHeight + 2 * layout.NoteHeight, ToolbarCpuList.Height(rows, layout), "room for the empty text");
            Assert.Throws<ArgumentNullException>(() => ToolbarCpuList.Rows(null, layout));
            Assert.AreEqual(ToolbarCpuList.EMPTY_NOTE, ToolbarCpuList.EmptyNote(false));
            StringAssert.Contains("Settings", ToolbarCpuList.EmptyNote(true), "in the editor, where the boot folder button is");
        }

        [Test]
        public void TheRowsText()
        {
            Assert.AreEqual("CX-4181", ToolbarCpuList.PartName("CX-4181 Scriptable Control System"));
            Assert.AreEqual("KAL9000", ToolbarCpuList.PartName("  KAL9000 "));
            Assert.AreEqual("CPU", ToolbarCpuList.PartName(null));
            Assert.AreEqual("CX-4181", ToolbarCpuList.CpuLabel(Cpu("ship"), " · "));
            Assert.AreEqual("CX-4181 · second", ToolbarCpuList.CpuLabel(Cpu("ship", "second"), " · "));

            ToolbarCpuEntry entry = Cpu("ship", null, "Running 0:/main.ks");
            Assert.AreEqual("Running 0:/main.ks", ToolbarCpuList.Detail(entry, " · "));
            entry.TelnetClients = 1;
            Assert.AreEqual("Running 0:/main.ks · 1 telnet client", ToolbarCpuList.Detail(entry, " · "));
            entry.TelnetClients = 2;
            Assert.AreEqual("Running 0:/main.ks - 2 telnet clients", ToolbarCpuList.Detail(entry, " - "));
            entry.HasStatus = false;
            Assert.AreEqual("2 telnet clients", ToolbarCpuList.Detail(entry, " · "), "no state yet");
            entry.TelnetClients = 0;
            Assert.AreEqual("", ToolbarCpuList.Detail(entry, " · "));
        }
    }

    [TestFixture]
    public class ToolbarSettingsTest
    {
        /// <summary>The keys as <c>kOS.Suffixed.Config</c> builds them, in its order (keep in step with Config.cs).</summary>
        internal static List<ConfigKey> GameKeys()
        {
            return new List<ConfigKey>
            {
                new ConfigKey("EnableTelnet", "TELNET", "Enable Telnet server", false, false, true, typeof(bool)),
                new ConfigKey("TelnetPort", "TPORT", "Telnet port number (must restart telnet to take effect)", 5410, 1024, 65535, typeof(int)),
                new ConfigKey("TelnetIPAddrString", "IPADDRESS", "Telnet IP address string (must restart telnet to take effect)", "127.0.0.1", "n/a", "n/a", typeof(string)),
                new ConfigKey("TerminalFontDefaultSize", "DEFAULTFONTSIZE", "Initial Terminal:CHARHEIGHT when a terminal is first opened", 12, 4, 48, typeof(int)),
                new ConfigKey("TerminalFontName", "FONTNAME", "Font Name for terminal window", "_not_chosen_yet_", "n/a", "n/a", typeof(string)),
                new ConfigKey("TerminalBrightness", "BRIGHTNESS", "Initial brightness setting for new terminals", 0.7d, 0d, 1d, typeof(double)),
                new ConfigKey("TerminalVisualBeep", "VISUALBEEP", "Initial Terminal:VISUALBEEP for new terminals (flash instead of a sound)", false, false, true, typeof(bool)),
                new ConfigKey("TerminalKeyClick", "KEYCLICK", "Terminal windows click on each key typed", false, false, true, typeof(bool)),
                new ConfigKey("TerminalDefaultWidth", "DEFAULTWIDTH", "Initial Terminal:WIDTH when a terminal is first opened", 50, 15, 255, typeof(int)),
                new ConfigKey("TerminalDefaultHeight", "DEFAULTHEIGHT", "Initial Terminal:HEIGHT when a terminal is first opened", 36, 3, 160, typeof(int)),
                new ConfigKey("SuppressAutopilot", "SUPPRESSAUTOPILOT", "Suppress all kOS autopiloting for emergency manual control", false, false, true, typeof(bool))
            };
        }

        private static string[] Aliases(ToolbarSettingSection section)
        {
            return section.Settings.Select(setting => setting.Key.Alias).ToArray();
        }

        [Test]
        public void TheGamesKeysInGroups()
        {
            List<ToolbarSettingSection> sections = ToolbarSettings.Sections(GameKeys());
            CollectionAssert.AreEqual(new[] { ToolbarSettingGroup.Terminal, ToolbarSettingGroup.Telnet }, sections.Select(s => s.Group).ToArray(), "no Other");
            CollectionAssert.AreEqual(new[] { "FONTNAME", "DEFAULTFONTSIZE", "DEFAULTWIDTH", "DEFAULTHEIGHT", "BRIGHTNESS", "VISUALBEEP", "KEYCLICK" }, Aliases(sections[0]));
            CollectionAssert.AreEqual(new[] { "TELNET", "IPADDRESS", "TPORT" }, Aliases(sections[1]));
            CollectionAssert.AreEqual(new[] { "Font", "Character size", "Width", "Height", "Brightness", "Visual beep", "Key click" },
                sections[0].Settings.Select(s => s.Label).ToArray());
            Assert.AreEqual("Terminal defaults", sections[0].Title);
            Assert.AreEqual("SUPPRESSAUTOPILOT", ToolbarSettings.FooterKey(GameKeys()).Alias);
        }

        [Test]
        public void EveryKeyIsShownOnce()
        {
            List<ConfigKey> keys = GameKeys();
            var shown = ToolbarSettings.Sections(keys).SelectMany(s => s.Settings).Select(s => s.Key).ToList();
            shown.Add(ToolbarSettings.FooterKey(keys));
            CollectionAssert.AreEquivalent(keys, shown);
            CollectionAssert.AllItemsAreUnique(shown);
        }

        [Test]
        public void AnUnknownKeyGoesToOther()
        {
            List<ConfigKey> keys = GameKeys();
            keys.Insert(2, new ConfigKey("SomeNewOption", "NEWOPTION", "A newer option", 3, 1, 9, typeof(int)));
            keys.Add(new ConfigKey("AnotherOption", "ANOTHER", "And another", true, false, true, typeof(bool)));
            List<ToolbarSettingSection> sections = ToolbarSettings.Sections(keys);
            ToolbarSettingSection other = sections.Last();
            Assert.AreEqual(ToolbarSettingGroup.Other, other.Group);
            CollectionAssert.AreEqual(new[] { "NEWOPTION", "ANOTHER" }, Aliases(other), "in the order given");
            Assert.AreEqual("NEWOPTION", other.Settings[0].Label, "labelled by the alias");
            Assert.AreEqual(3, sections.Count);
        }

        [Test]
        public void KeysMatchIgnoringCaseAndOnce()
        {
            var keys = new List<ConfigKey>
            {
                new ConfigKey("terminalkeyclick", "KEYCLICK", "click", false, false, true, typeof(bool)),
                new ConfigKey("TerminalKeyClick", "KEYCLICK2", "a duplicate name", false, false, true, typeof(bool)),
                null
            };
            List<ToolbarSettingSection> sections = ToolbarSettings.Sections(keys);
            Assert.AreEqual(1, sections.Count);
            CollectionAssert.AreEqual(new[] { "KEYCLICK" }, Aliases(sections[0]));
            Assert.AreEqual("Key click", sections[0].Settings[0].Label);
            Assert.IsNull(ToolbarSettings.FooterKey(keys));
            Assert.AreEqual(0, ToolbarSettings.Sections(new ConfigKey[0]).Count);
            Assert.Throws<ArgumentNullException>(() => ToolbarSettings.Sections(null));
        }

        [Test]
        public void TheDescriptionNamesTheScriptSuffixAndRange()
        {
            Dictionary<string, ToolbarSetting> settings = ToolbarSettings.Sections(GameKeys()).SelectMany(s => s.Settings).ToDictionary(s => s.Key.Alias);
            Assert.AreEqual("CONFIG:TPORT · 1024 to 65535", ToolbarSettings.Detail(settings["TPORT"], " · "));
            Assert.AreEqual("CONFIG:DEFAULTFONTSIZE - 4 to 48", ToolbarSettings.Detail(settings["DEFAULTFONTSIZE"], " - "));
            Assert.AreEqual("CONFIG:BRIGHTNESS · 0 to 1", ToolbarSettings.Detail(settings["BRIGHTNESS"], " · "));
            Assert.AreEqual("CONFIG:KEYCLICK", ToolbarSettings.Detail(settings["KEYCLICK"], " · "));
            Assert.AreEqual("CONFIG:FONTNAME", ToolbarSettings.Detail(settings["FONTNAME"], " · "));
            Assert.AreEqual("", ToolbarSettings.Detail(null, " · "));
        }
    }

    [TestFixture]
    public class ToolbarNumberFieldTest
    {
        private int value;

        private bool Frame(ToolbarNumberField field, string id, string typed, bool focus, bool enter = false)
        {
            return field.Edited(id, typed, focus, enter, out value);
        }

        [Test]
        public void EnterApplies()
        {
            var field = new ToolbarNumberField();
            Assert.AreEqual("5410", field.Shown("port", 5410));
            Assert.IsFalse(Frame(field, "port", "5410", true));
            Assert.IsFalse(Frame(field, "port", "541", true), "typing");
            Assert.AreEqual("541", field.Shown("port", 5410), "the typing is shown");
            Assert.IsTrue(Frame(field, "port", "5411", true, true));
            Assert.AreEqual(5411, value);
            Assert.IsFalse(field.IsEditing("port"));
        }

        [Test]
        public void LosingTheKeyboardApplies()
        {
            var field = new ToolbarNumberField();
            Frame(field, "width", "80", true);
            Assert.IsTrue(Frame(field, "width", "80", false), "clicked elsewhere");
            Assert.AreEqual(80, value);
            Assert.IsFalse(Frame(field, "width", "50", false), "nothing more once applied");
            Assert.AreEqual("50", field.Shown("width", 50));
        }

        [Test]
        public void AnotherFieldTakingTheKeyboardAppliesTheFirst()
        {
            var field = new ToolbarNumberField();
            Frame(field, "width", "80", true);
            Assert.IsTrue(Frame(field, "width", "80", false), "the first is drawn first, without the keyboard now");
            Assert.AreEqual(80, value);
            Assert.IsFalse(Frame(field, "height", "36", true));
            Assert.IsTrue(field.IsEditing("height"));
        }

        [Test]
        public void BlankOrBadTypingIsDropped()
        {
            var field = new ToolbarNumberField();
            foreach (string typed in new[] { "", "  ", "12a", "99999999999" })
            {
                Frame(field, "width", typed, true);
                Assert.IsFalse(Frame(field, "width", typed, false), "[" + typed + "]");
                Assert.AreEqual("50", field.Shown("width", 50), "the value shows again: [" + typed + "]");
            }
            Frame(field, "width", " 60 ", true);
            Assert.IsTrue(Frame(field, "width", " 60 ", false));
            Assert.AreEqual(60, value);
        }

        [Test]
        public void TypingLeftWhenTheFieldIsNoLongerDrawn()
        {
            var field = new ToolbarNumberField();
            string id;
            Assert.IsFalse(field.FinishPending(out id, out value));
            Assert.IsNull(id);
            Frame(field, "height", "40", true);
            Assert.IsTrue(field.FinishPending(out id, out value));
            Assert.AreEqual("height", id);
            Assert.AreEqual(40, value);
            Assert.IsFalse(field.FinishPending(out id, out value), "once");
        }
    }

    [TestFixture]
    public class TelnetPanelStatusTest
    {
        [Test]
        public void TheTelnetLine()
        {
            Assert.AreEqual("Telnet not started yet", TelnetPanelStatus.Text(false, true, "127.0.0.1", 5410, 1, " · "));
            Assert.AreEqual("Telnet off", TelnetPanelStatus.Text(true, false, "127.0.0.1", 5410, 0, " · "));
            Assert.AreEqual("Telnet 127.0.0.1:5410 · no clients", TelnetPanelStatus.Text(true, true, "127.0.0.1", 5410, 0, " · "));
            Assert.AreEqual("Telnet 127.0.0.1:5410 - 1 client", TelnetPanelStatus.Text(true, true, "127.0.0.1", 5410, 1, " - "));
            Assert.AreEqual("Telnet [::1]:5411 · 3 clients", TelnetPanelStatus.Text(true, true, "::1", 5411, 3, " · "));
            Assert.AreEqual("?:5410", TelnetPanelStatus.Endpoint(null, 5410));
        }

        [Test]
        public void ARestartIsNeededForAnotherPort()
        {
            Assert.IsFalse(TelnetPanelStatus.RestartNeeded(true, 5410, 5410));
            Assert.IsTrue(TelnetPanelStatus.RestartNeeded(true, 5410, 5411));
            Assert.IsFalse(TelnetPanelStatus.RestartNeeded(false, 5410, 5411), "off: it reads the port when it starts");
            Assert.AreEqual("Restart telnet to use port 5411", TelnetPanelStatus.RestartText(5411));
        }
    }
}
