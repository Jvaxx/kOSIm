using System;
using System.Globalization;

namespace kOS.Safe.Encapsulation.Suffixes
{
    /// <summary>
    /// Typing rules for an integer <see cref="ConfigKey"/> in the toolbar settings panel
    /// (PANEL-02). The panel used a fixed 4-character limit, so a valid five-digit telnet
    /// port such as 54100 could not be typed. The limit now follows the key's range; the
    /// typed value is still clamped by <see cref="ConfigKey"/> when committed.
    /// </summary>
    public static class ConfigIntInput
    {
        /// <summary>The longest text any value in the key's range needs (sign included).</summary>
        public static int MaxLength(ConfigKey key)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            int length = 1;
            if (key.MinValue is int)
                length = Math.Max(length, ((int)key.MinValue).ToString(CultureInfo.InvariantCulture).Length);
            if (key.MaxValue is int)
                length = Math.Max(length, ((int)key.MaxValue).ToString(CultureInfo.InvariantCulture).Length);
            return length;
        }

        /// <summary>
        /// Read the field's text as the panel always did: surrounding spaces ignored, empty
        /// text read as 0 (so the user can clear the field while typing), otherwise an
        /// invariant-culture integer. False for anything else; the panel then drops the typing.
        /// </summary>
        public static bool TryParse(string text, out int value)
        {
            string trimmed = (text ?? string.Empty).Trim(' ');
            if (trimmed.Length == 0)
            {
                value = 0;
                return true;
            }
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
