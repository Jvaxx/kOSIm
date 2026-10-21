using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using kOS.Safe.Persistence;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The GUI terminal's geometry presets: three slots, each a complete
    /// <see cref="TerminalGeometry"/> (position, cells, character height) or empty. One set for
    /// every CPU, vessel and save (<see cref="TerminalPresetFile"/>); the header's preset buttons
    /// apply a slot (left click) or save the window's geometry into it (right click).
    /// </summary>
    public sealed class TerminalPresets
    {
        public const string HEADER = "# kOS terminal presets: per slot, position in screen pixels, size in cells, character height in pixels";

        /// <summary>The slots are numbered 1 to this.</summary>
        public const int SLOT_COUNT = 3;

        private readonly TerminalGeometry[] slots = new TerminalGeometry[SLOT_COUNT];

        /// <summary>True when <paramref name="slot"/> holds a geometry.</summary>
        public bool IsFilled(int slot)
        {
            return slots[Index(slot)] != null;
        }

        /// <summary>A copy of the slot's geometry; null when it is empty.</summary>
        public TerminalGeometry Get(int slot)
        {
            TerminalGeometry geometry = slots[Index(slot)];
            return geometry == null ? null : geometry.Copy();
        }

        /// <summary>Keep a copy of <paramref name="geometry"/> in the slot; null empties it.</summary>
        /// <exception cref="ArgumentException">the geometry is not complete</exception>
        public void Set(int slot, TerminalGeometry geometry)
        {
            int index = Index(slot);
            if (geometry != null && !geometry.IsComplete)
                throw new ArgumentException("A preset holds a complete geometry: " + geometry, "geometry");
            slots[index] = geometry == null ? null : geometry.Copy();
        }

        private static int Index(int slot)
        {
            if (slot < 1 || slot > SLOT_COUNT)
                throw new ArgumentOutOfRangeException("slot", slot, "A preset slot is 1 to " + SLOT_COUNT);
            return slot - 1;
        }

        /// <summary>
        /// The file's text: a header line, then for each filled slot the
        /// <see cref="TerminalGeometry"/> lines with the slot's number before each key
        /// (<c>1.x = 120</c>, <c>1.columns = 50</c>).
        /// </summary>
        public static string Format(TerminalPresets presets)
        {
            if (presets == null) throw new ArgumentNullException("presets");
            var text = new StringBuilder(HEADER).Append('\n');
            for (int slot = 1; slot <= SLOT_COUNT; ++slot)
            {
                TerminalGeometry geometry = presets.slots[slot - 1];
                if (geometry != null)
                    TerminalGeometry.AppendValues(text, slot.ToString(CultureInfo.InvariantCulture) + ".", geometry);
            }
            return text.ToString();
        }

        /// <summary>
        /// The presets in a saved file, as tolerant as <see cref="TerminalGeometry.Parse"/>, with
        /// the same limits per value. Keys without a known slot number or value name are ignored.
        /// A slot counts only when all its values are known and believed; otherwise it is empty,
        /// so a preset never applies half a geometry.
        /// </summary>
        public static TerminalPresets Parse(string text)
        {
            var read = new TerminalGeometry[SLOT_COUNT];
            foreach (KeyValuePair<string, string> entry in TerminalGeometry.Entries(text))
            {
                int dot = entry.Key.IndexOf('.');
                if (dot <= 0) continue;
                int slot;
                if (!int.TryParse(entry.Key.Substring(0, dot).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out slot) ||
                    slot < 1 || slot > SLOT_COUNT)
                    continue;
                if (read[slot - 1] == null)
                    read[slot - 1] = new TerminalGeometry();
                read[slot - 1].SetValue(entry.Key.Substring(dot + 1).Trim(), entry.Value);
            }
            var presets = new TerminalPresets();
            for (int index = 0; index < SLOT_COUNT; ++index)
                if (read[index] != null && read[index].IsComplete)
                    presets.slots[index] = read[index];
            return presets;
        }
    }

    /// <summary>
    /// The terminal presets as one text file for the game
    /// (<c>GameData/kOS/Plugins/PluginData/kOS/terminal-presets.txt</c>, next to
    /// <c>config.xml</c>), saved and read as a <see cref="CpuTextFile"/>: a failed save leaves
    /// the old file, failures are logged once, and a file over <see cref="MAX_FILE_BYTES"/> is
    /// not read. Read once on first use and kept; each save writes every slot. Main thread only.
    /// </summary>
    public sealed class TerminalPresetFile
    {
        /// <summary>A larger file is not read (three presets are under 400 bytes).</summary>
        public const long MAX_FILE_BYTES = 4096;

        private readonly CpuTextFile file;
        private TerminalPresets presets; // null until read

        /// <param name="pathProvider">the file's path, asked once on first use; null for no file (presets last for the game only)</param>
        public TerminalPresetFile(Func<string> pathProvider) : this(pathProvider, new ArchiveFileOperations())
        {
        }

        internal TerminalPresetFile(Func<string> pathProvider, ArchiveFileOperations files)
        {
            file = new CpuTextFile("Terminal presets", pathProvider, MAX_FILE_BYTES, files);
        }

        /// <summary>The file's path once resolved; null before, or when there is none.</summary>
        public string Path { get { return file.Path; } }

        private TerminalPresets Presets
        {
            get
            {
                if (presets == null)
                {
                    string text = file.Read();
                    presets = text == null ? new TerminalPresets() : TerminalPresets.Parse(text);
                }
                return presets;
            }
        }

        /// <summary>True when <paramref name="slot"/> (1 to <see cref="TerminalPresets.SLOT_COUNT"/>) holds a geometry.</summary>
        public bool IsFilled(int slot)
        {
            return Presets.IsFilled(slot);
        }

        /// <summary>A copy of the slot's geometry; null when it is empty.</summary>
        public TerminalGeometry Get(int slot)
        {
            return Presets.Get(slot);
        }

        /// <summary>
        /// Keep <paramref name="geometry"/> (complete) in the slot and save every slot. True once
        /// saved; false when there is no file or the save failed (logged once; the old file is
        /// kept): the slot is still set for this game.
        /// </summary>
        public bool Save(int slot, TerminalGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException("geometry");
            TerminalPresets kept = Presets;
            kept.Set(slot, geometry);
            return file.Write(TerminalPresets.Format(kept));
        }
    }
}
