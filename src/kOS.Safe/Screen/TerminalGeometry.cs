using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using kOS.Safe.Encapsulation;
using kOS.Safe.Persistence;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A CPU's GUI terminal window as saved between game sessions (Phase 4, step 2): its
    /// position in screen pixels, its size in cells and the character height. A value that
    /// is not known (missing or rejected from a file) is null, and the terminal keeps its
    /// default for it.
    /// </summary>
    public sealed class TerminalGeometry : IEquatable<TerminalGeometry>
    {
        public const string HEADER = "# kOS terminal window: position in screen pixels, size in cells, character height in pixels";

        /// <summary>A saved position further than this from the screen's origin is not believed.</summary>
        public const int MAX_COORDINATE = 100000;

        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Columns { get; set; }
        public int? Rows { get; set; }
        public int? CharacterHeight { get; set; }

        public bool Equals(TerminalGeometry other)
        {
            return other != null && X == other.X && Y == other.Y && Columns == other.Columns &&
                Rows == other.Rows && CharacterHeight == other.CharacterHeight;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TerminalGeometry);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Columns.GetHashCode();
                hash = hash * 31 + Rows.GetHashCode();
                return hash * 31 + CharacterHeight.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Format(this).Replace('\n', ' ');
        }

        /// <summary>The file's text: a header line, then <c>key = value</c> lines for the known values.</summary>
        public static string Format(TerminalGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException("geometry");
            var text = new StringBuilder(HEADER).Append('\n');
            AppendValues(text, string.Empty, geometry);
            return text.ToString();
        }

        /// <summary>The known values' lines, each key after <paramref name="prefix"/> (a preset's <c>1.</c>).</summary>
        internal static void AppendValues(StringBuilder text, string prefix, TerminalGeometry geometry)
        {
            AppendValue(text, prefix, "x", geometry.X);
            AppendValue(text, prefix, "y", geometry.Y);
            AppendValue(text, prefix, "columns", geometry.Columns);
            AppendValue(text, prefix, "rows", geometry.Rows);
            AppendValue(text, prefix, "charheight", geometry.CharacterHeight);
        }

        private static void AppendValue(StringBuilder text, string prefix, string key, int? value)
        {
            if (value.HasValue)
                text.Append(prefix).Append(key).Append(" = ").Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        /// <summary>
        /// The geometry in a saved file. Tolerant: comments (<c>#</c>), empty lines, CR LF line
        /// ends, unknown keys and spaces around <c>=</c> are accepted, keys ignore case, and the
        /// last of a repeated key wins. A value that is not a whole number, or is outside the
        /// limits a script has (<c>TERMINAL:WIDTH</c>, <c>HEIGHT</c>, <c>CHARHEIGHT</c>) or
        /// <see cref="MAX_COORDINATE"/>, is left unknown.
        /// </summary>
        public static TerminalGeometry Parse(string text)
        {
            var geometry = new TerminalGeometry();
            foreach (KeyValuePair<string, string> entry in Entries(text))
                geometry.SetValue(entry.Key, entry.Value);
            return geometry;
        }

        /// <summary>True when every value is known (a preset holds only complete geometries).</summary>
        public bool IsComplete
        {
            get { return X.HasValue && Y.HasValue && Columns.HasValue && Rows.HasValue && CharacterHeight.HasValue; }
        }

        /// <summary>A geometry with the same values.</summary>
        public TerminalGeometry Copy()
        {
            return new TerminalGeometry { X = X, Y = Y, Columns = Columns, Rows = Rows, CharacterHeight = CharacterHeight };
        }

        /// <summary>
        /// The <c>key = value</c> lines of a saved file, in order, the key trimmed and in lower
        /// case, the value trimmed; comments (<c>#</c>), empty lines and lines without a key are
        /// left out. Shared with <see cref="TerminalPresets.Parse"/>.
        /// </summary>
        internal static IEnumerable<KeyValuePair<string, string>> Entries(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                yield return new KeyValuePair<string, string>(line.Substring(0, equals).Trim().ToLowerInvariant(), line.Substring(equals + 1).Trim());
            }
        }

        /// <summary>
        /// Set the value a file's <paramref name="key"/> (in lower case) names: unknown when it
        /// cannot be believed (the limits of <see cref="Parse"/>). False for an unknown key.
        /// </summary>
        internal bool SetValue(string key, string value)
        {
            switch (key)
            {
                case "x": X = Number(value, -MAX_COORDINATE, MAX_COORDINATE); return true;
                case "y": Y = Number(value, -MAX_COORDINATE, MAX_COORDINATE); return true;
                case "columns": Columns = Number(value, TerminalStruct.MINCOLUMNS, TerminalStruct.MAXCOLUMNS); return true;
                case "rows": Rows = Number(value, TerminalStruct.MINROWS, TerminalStruct.MAXROWS); return true;
                case "charheight": CharacterHeight = Number(value, TerminalStruct.MINCHARPIXELS, TerminalStruct.MAXCHARPIXELS); return true;
                default: return false;
            }
        }

        private static int? Number(string text, int minimum, int maximum)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return null;
            if (value < minimum || value > maximum) return null;
            return value;
        }
    }

    /// <summary>Where a GUI terminal keeps its <see cref="TerminalGeometry"/> between game sessions.</summary>
    public interface ITerminalGeometryStore
    {
        /// <summary>The saved geometry; null when there is none or it cannot be read.</summary>
        TerminalGeometry Load();

        /// <summary>Replace the saved geometry. Must not throw for I/O failures.</summary>
        void Save(TerminalGeometry geometry);
    }

    /// <summary>
    /// A CPU's terminal geometry as a text file next to its command history (Phase 4, step 2;
    /// <see cref="TerminalGeometry.Format"/>), saved and read as a <see cref="CpuTextFile"/>:
    /// a failed save leaves the old file, and failures are logged once and never shown on the
    /// terminal. Main thread only.
    /// </summary>
    public sealed class TerminalGeometryFile : ITerminalGeometryStore
    {
        /// <summary>A larger file is not read (a saved geometry is under 200 bytes).</summary>
        public const long MAX_FILE_BYTES = 4096;

        private readonly CpuTextFile file;

        /// <param name="pathProvider">the file's path, asked once on first use; null for no file (nothing is kept)</param>
        public TerminalGeometryFile(Func<string> pathProvider) : this(pathProvider, new ArchiveFileOperations())
        {
        }

        internal TerminalGeometryFile(Func<string> pathProvider, ArchiveFileOperations files)
        {
            file = new CpuTextFile("Terminal geometry", pathProvider, MAX_FILE_BYTES, files);
        }

        /// <summary>The file's path once resolved; null before, or when there is none.</summary>
        public string Path { get { return file.Path; } }

        public TerminalGeometry Load()
        {
            string text = file.Read();
            return text == null ? null : TerminalGeometry.Parse(text);
        }

        public void Save(TerminalGeometry geometry)
        {
            file.Write(TerminalGeometry.Format(geometry));
        }
    }
}
