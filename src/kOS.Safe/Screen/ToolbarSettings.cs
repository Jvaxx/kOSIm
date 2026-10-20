using System;
using System.Collections.Generic;
using System.Globalization;
using kOS.Safe.Encapsulation.Suffixes;

namespace kOS.Safe.Screen
{
    public enum ToolbarSettingGroup
    {
        Terminal,
        Telnet,
        /// <summary>A config key the panel does not know: drawn by its type, so that none is hidden.</summary>
        Other
    }

    /// <summary>A config key on the settings tab, with its label in words.</summary>
    public sealed class ToolbarSetting
    {
        public ConfigKey Key { get; private set; }
        public ToolbarSettingGroup Group { get; private set; }
        public string Label { get; private set; }

        public ToolbarSetting(ConfigKey key, ToolbarSettingGroup group, string label)
        {
            if (key == null) throw new ArgumentNullException("key");
            Key = key;
            Group = group;
            Label = label;
        }

        /// <summary>How a script reads it: <c>CONFIG:DEFAULTFONTSIZE</c>.</summary>
        public string ScriptName
        {
            get { return "CONFIG:" + Key.Alias; }
        }
    }

    public sealed class ToolbarSettingSection
    {
        public ToolbarSettingGroup Group { get; private set; }
        public string Title { get; private set; }
        public List<ToolbarSetting> Settings { get; private set; }

        public ToolbarSettingSection(ToolbarSettingGroup group, string title)
        {
            Group = group;
            Title = title;
            Settings = new List<ToolbarSetting>();
        }
    }

    /// <summary>
    /// The toolbar panel's settings tab (Phase 4, step 4): the config keys (<c>config.xml</c>)
    /// in groups, labelled in words, in this catalog's order; <c>SuppressAutopilot</c> is the
    /// footer's button instead. A key the catalog does not know goes to Other, labelled by its
    /// alias, in the order given. Keys are matched by name, ignoring case (as <c>Config</c>).
    /// </summary>
    public static class ToolbarSettings
    {
        public const string SUPPRESS_AUTOPILOT = "SuppressAutopilot";
        public const string FONT_NAME = "TerminalFontName";
        public const string TELNET_ADDRESS = "TelnetIPAddrString";
        public const string TELNET_PORT = "TelnetPort";

        public const string GENERAL_NOTE = "Saved for all games. More kOS options: KSP's difficulty settings.";

        private struct Known
        {
            public readonly string Name;
            public readonly ToolbarSettingGroup Group;
            public readonly string Label;

            public Known(string name, ToolbarSettingGroup group, string label)
            {
                Name = name;
                Group = group;
                Label = label;
            }
        }

        private static readonly Known[] catalog =
        {
            new Known(FONT_NAME, ToolbarSettingGroup.Terminal, "Font"),
            new Known("TerminalFontDefaultSize", ToolbarSettingGroup.Terminal, "Character size"),
            new Known("TerminalDefaultWidth", ToolbarSettingGroup.Terminal, "Width"),
            new Known("TerminalDefaultHeight", ToolbarSettingGroup.Terminal, "Height"),
            new Known("TerminalBrightness", ToolbarSettingGroup.Terminal, "Brightness"),
            new Known("TerminalVisualBeep", ToolbarSettingGroup.Terminal, "Visual beep"),
            new Known("TerminalKeyClick", ToolbarSettingGroup.Terminal, "Key click"),
            new Known("EnableTelnet", ToolbarSettingGroup.Telnet, "Telnet server"),
            new Known(TELNET_ADDRESS, ToolbarSettingGroup.Telnet, "Address"),
            new Known(TELNET_PORT, ToolbarSettingGroup.Telnet, "Port")
        };

        public static string Title(ToolbarSettingGroup group)
        {
            switch (group)
            {
                case ToolbarSettingGroup.Terminal: return "Terminal defaults";
                case ToolbarSettingGroup.Telnet: return "Telnet";
                default: return "Other";
            }
        }

        public static bool Is(ConfigKey key, string name)
        {
            return key != null && string.Equals(key.StringKey, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The non-empty sections, Terminal, Telnet, Other, each setting once.</summary>
        public static List<ToolbarSettingSection> Sections(IEnumerable<ConfigKey> keys)
        {
            if (keys == null) throw new ArgumentNullException("keys");
            var known = new ToolbarSetting[catalog.Length];
            var others = new List<ToolbarSetting>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigKey key in keys)
            {
                if (key == null || Is(key, SUPPRESS_AUTOPILOT) || !seen.Add(key.StringKey ?? string.Empty)) continue;
                int index = Array.FindIndex(catalog, entry => Is(key, entry.Name));
                if (index >= 0)
                    known[index] = new ToolbarSetting(key, catalog[index].Group, catalog[index].Label);
                else
                    others.Add(new ToolbarSetting(key, ToolbarSettingGroup.Other, key.Alias));
            }

            var sections = new List<ToolbarSettingSection>();
            foreach (ToolbarSettingGroup group in new[] { ToolbarSettingGroup.Terminal, ToolbarSettingGroup.Telnet })
            {
                var section = new ToolbarSettingSection(group, Title(group));
                foreach (ToolbarSetting setting in known)
                    if (setting != null && setting.Group == group)
                        section.Settings.Add(setting);
                if (section.Settings.Count > 0)
                    sections.Add(section);
            }
            if (others.Count > 0)
            {
                var other = new ToolbarSettingSection(ToolbarSettingGroup.Other, Title(ToolbarSettingGroup.Other));
                other.Settings.AddRange(others);
                sections.Add(other);
            }
            return sections;
        }

        /// <summary>The footer's key (<c>SuppressAutopilot</c>), or null.</summary>
        public static ConfigKey FooterKey(IEnumerable<ConfigKey> keys)
        {
            if (keys == null) return null;
            foreach (ConfigKey key in keys)
                if (Is(key, SUPPRESS_AUTOPILOT))
                    return key;
            return null;
        }

        /// <summary>The description's second line: the script name, and the range of a number (<c>CONFIG:TPORT · 1024 to 65535</c>).</summary>
        public static string Detail(ToolbarSetting setting, string separator)
        {
            if (setting == null) return string.Empty;
            string range = Range(setting.Key);
            return range == null ? setting.ScriptName : setting.ScriptName + separator + range;
        }

        private static string Range(ConfigKey key)
        {
            if (key.ValType == typeof(int) && key.MinValue is int && key.MaxValue is int)
                return string.Format(CultureInfo.InvariantCulture, "{0} to {1}", key.MinValue, key.MaxValue);
            if ((key.ValType == typeof(double) || key.ValType == typeof(float)) && IsNumber(key.MinValue) && IsNumber(key.MaxValue))
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} to {1:0.##}", Convert.ToDouble(key.MinValue), Convert.ToDouble(key.MaxValue));
            return null;
        }

        private static bool IsNumber(object value)
        {
            return value is double || value is float || value is int;
        }
    }

    /// <summary>
    /// A number field's typing on the settings tab (Phase 4 step 4, Q3): kept while the field
    /// has the keyboard, applied on Enter or when it loses the keyboard (upstream applied only
    /// on Enter and dropped the typing otherwise). Typing that is blank or not a whole number
    /// is dropped and the setting keeps its value; <c>Config</c> clamps what is applied.
    /// </summary>
    public sealed class ToolbarNumberField
    {
        private string editing; // the field being typed in
        private string text;

        public bool IsEditing(string field)
        {
            return field != null && field == editing;
        }

        /// <summary>What the field shows: the typing while it is edited, else the value.</summary>
        public string Shown(string field, int value)
        {
            return IsEditing(field) ? text : value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>After the field was drawn: <paramref name="typed"/> is what it holds now.</summary>
        /// <returns>true with the <paramref name="value"/> to apply</returns>
        public bool Edited(string field, string typed, bool hasFocus, bool enterPressed, out int value)
        {
            value = 0;
            if (field == null) return false;
            if (hasFocus)
            {
                editing = field;
                text = typed ?? string.Empty;
                return enterPressed && Finish(out value);
            }
            if (!IsEditing(field)) return false;
            text = typed ?? text;
            return Finish(out value); // the keyboard went elsewhere
        }

        /// <summary>
        /// The field being edited when it is no longer drawn (the tab changed, the panel closed):
        /// its typing is applied as when it loses the keyboard.
        /// </summary>
        public bool FinishPending(out string field, out int value)
        {
            field = editing;
            if (field == null)
            {
                value = 0;
                return false;
            }
            return Finish(out value);
        }

        private bool Finish(out int value)
        {
            string typed = text;
            editing = null;
            text = null;
            value = 0;
            if (string.IsNullOrEmpty(typed) || typed.Trim().Length == 0) return false;
            return ConfigIntInput.TryParse(typed, out value);
        }
    }
}
