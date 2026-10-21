using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>One CPU as the toolbar panel's CPUs tab shows it (Phase 4, step 4).</summary>
    public struct ToolbarCpuEntry
    {
        /// <summary>Its vessel, compared with <c>Equals</c> (the game passes the <c>Vessel</c>); null when it has none.</summary>
        public object Vessel;
        public string VesselName;
        /// <summary>The part's title, such as <c>CX-4181 Scriptable Control System</c>.</summary>
        public string PartTitle;
        /// <summary>Its name tag; null or empty when it has none.</summary>
        public string Tag;
        /// <summary>False until the terminal has worked out the D2 state (or when that failed).</summary>
        public bool HasStatus;
        public StatusKind Kind;
        /// <summary>The D2 state, as the terminal's strip shows it (<see cref="TerminalStatus.StateText"/>).</summary>
        public string StateText;
        public bool WindowOpen;
        public int TelnetClients;
    }

    public enum ToolbarCpuRowKind
    {
        Vessel,
        Cpu
    }

    /// <summary>A row of the CPUs tab, at <see cref="Y"/> from the content's top.</summary>
    public struct ToolbarCpuRow
    {
        public ToolbarCpuRowKind Kind;
        /// <summary>The entry the row is for (a vessel's heading: its first CPU).</summary>
        public int Entry;
        public float Y;
        public float Height;
    }

    /// <summary>
    /// The CPUs tab (Phase 4, step 4): the CPUs in kOS's order (active vessel first, then by
    /// distance; on a vessel, nearest the root first), a heading at each new vessel, one row a
    /// CPU. A CPU without a vessel gets no heading (as upstream).
    /// </summary>
    public static class ToolbarCpuList
    {
        public const string EMPTY_TEXT = "No loaded CPUs.";
        public const string EMPTY_NOTE = "CPUs far away (on rails) are not listed.";
        public const string EDITOR_NOTE = "The editor lists no CPUs; the boot folder is under Settings.";

        /// <summary>The line under <see cref="EMPTY_TEXT"/>.</summary>
        public static string EmptyNote(bool inEditor)
        {
            return inEditor ? EDITOR_NOTE : EMPTY_NOTE;
        }

        public static List<ToolbarCpuRow> Rows(IList<ToolbarCpuEntry> entries, ToolbarPanelLayout layout)
        {
            if (entries == null) throw new ArgumentNullException("entries");
            if (layout == null) throw new ArgumentNullException("layout");
            var rows = new List<ToolbarCpuRow>(entries.Count * 2);
            float y = 0;
            object previousVessel = null;
            for (int i = 0; i < entries.Count; ++i)
            {
                object vessel = entries[i].Vessel;
                if (vessel != null && !vessel.Equals(previousVessel))
                {
                    rows.Add(new ToolbarCpuRow { Kind = ToolbarCpuRowKind.Vessel, Entry = i, Y = y, Height = layout.VesselRowHeight });
                    y += layout.VesselRowHeight;
                }
                previousVessel = vessel;
                rows.Add(new ToolbarCpuRow { Kind = ToolbarCpuRowKind.Cpu, Entry = i, Y = y, Height = layout.CpuRowHeight });
                y += layout.CpuRowHeight;
            }
            return rows;
        }

        /// <summary>The rows' total height; with none, the empty text and its note.</summary>
        public static float Height(IList<ToolbarCpuRow> rows, ToolbarPanelLayout layout)
        {
            if (rows == null || rows.Count == 0) return layout.SettingRowHeight + 2 * layout.NoteHeight;
            ToolbarCpuRow last = rows[rows.Count - 1];
            return last.Y + last.Height;
        }

        /// <summary>The row's first line: the part's name, then its tag (<c>CX-4181 · probe</c>).</summary>
        public static string CpuLabel(ToolbarCpuEntry entry, string separator)
        {
            string name = CpuNames.PartName(entry.PartTitle);
            return string.IsNullOrEmpty(entry.Tag) ? name : name + separator + entry.Tag;
        }

        /// <summary>The row's second line: the state, then the telnet clients when attached.</summary>
        public static string Detail(ToolbarCpuEntry entry, string separator)
        {
            string state = entry.HasStatus ? (entry.StateText ?? string.Empty) : string.Empty;
            if (entry.TelnetClients <= 0) return state;
            string telnet = entry.TelnetClients == 1 ? "1 telnet client" : entry.TelnetClients + " telnet clients";
            return state.Length == 0 ? telnet : state + separator + telnet;
        }
    }
}
