namespace kOS.Safe.Screen
{
    /// <summary>How a kOS CPU is named in its titles, the toolbar panel and the telnet menu.</summary>
    public static class CpuNames
    {
        /// <summary>
        /// The part's name: the first word of its title (<c>CX-4181</c>), as upstream's titles,
        /// panel and telnet menu; <c>CPU</c> for an empty title. One place since review D-03.
        /// </summary>
        public static string PartName(string partTitle)
        {
            string title = (partTitle ?? string.Empty).Trim();
            if (title.Length == 0) return "CPU";
            int space = title.IndexOf(' ');
            return space < 0 ? title : title.Substring(0, space);
        }
    }
}
