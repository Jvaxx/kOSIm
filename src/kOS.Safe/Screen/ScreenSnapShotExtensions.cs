namespace kOS.Safe.Screen
{
    /// <summary>Unity-independent read helpers; does not change the public snapshot interface.</summary>
    public static class ScreenSnapShotExtensions
    {
        /// <summary>
        /// Read a cursor cell using only snapshot coordinates/content, clipped to both
        /// the snapshot and the current viewer's grid. Invalid cursors are hidden, not moved.
        /// Null cells display as spaces, just as they do in rendered terminal rows.
        /// This does not make mutable snapshot rows thread-safe or immutable.
        /// </summary>
        public static bool TryGetCursorCharacter(this IScreenSnapShot snapshot, int visibleRows, int visibleColumns, out char character)
        {
            character = '\0';
            if (snapshot == null || visibleRows <= 0 || visibleColumns <= 0) return false;

            int row = snapshot.CursorRow;
            int column = snapshot.CursorColumn;
            var buffer = snapshot.Buffer;
            if (buffer == null || row < 0 || column < 0 || row >= snapshot.RowCount ||
                row >= visibleRows || row >= buffer.Count || column >= visibleColumns)
                return false;

            var line = buffer[row];
            if (line == null || column >= line.Length) return false;

            character = line[column];
            if (character == '\0') character = ' ';
            return true;
        }
    }
}
