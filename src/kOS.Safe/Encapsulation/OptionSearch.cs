using System;
using System.Collections.Generic;

namespace kOS.Safe.Encapsulation
{
    /// <summary>
    /// Finds the option a script selects in a GUI popup menu (<c>POPUPMENU:VALUE</c>), as
    /// <c>PopupMenu.Choose()</c> always matched: the same object first, otherwise the first
    /// option whose shown text equals the value's (ordinal, case-sensitive), otherwise -1.
    /// <para>
    /// WIDGET-01: <c>Choose()</c> used the menu's public <c>Index</c> as its loop counter, so
    /// one selection scheduled a change callback for every option it passed (and for -1 when
    /// the value was missing). Searching here leaves the menu to commit the result once.
    /// </para>
    /// </summary>
    public static class OptionSearch
    {
        /// <param name="display">The text the menu shows for an option (its <c>OPTIONSUFFIX</c>).</param>
        public static int IndexOf(IList<Structure> options, Structure value, Func<Structure, string> display)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            if (display == null)
                throw new ArgumentNullException("display");
            for (int i = 0; i < options.Count; ++i)
                if (ReferenceEquals(options[i], value))
                    return i;
            if (value == null)
                return -1;
            string shown = display(value);
            for (int i = 0; i < options.Count; ++i)
                if (string.Equals(display(options[i]), shown, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
