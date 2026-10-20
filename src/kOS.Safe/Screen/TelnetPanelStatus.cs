using System.Globalization;

namespace kOS.Safe.Screen
{
    /// <summary>The toolbar panel's telnet line and its restart button (Phase 4, step 4).</summary>
    public static class TelnetPanelStatus
    {
        /// <param name="available">false while the telnet server object does not exist yet (loading)</param>
        public static string Text(bool available, bool listening, string address, int port, int clients, string separator)
        {
            if (!available) return "Telnet not started yet";
            if (!listening) return "Telnet off";
            return "Telnet " + Endpoint(address, port) + separator + Clients(clients);
        }

        /// <summary><c>127.0.0.1:5410</c>; an IPv6 address in brackets (<c>[::1]:5410</c>).</summary>
        public static string Endpoint(string address, int port)
        {
            string host = string.IsNullOrEmpty(address) ? "?" : address;
            if (host.IndexOf(':') >= 0) host = "[" + host + "]";
            return host + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        public static string Clients(int clients)
        {
            if (clients <= 0) return "no clients";
            return clients == 1 ? "1 client" : clients.ToString(CultureInfo.InvariantCulture) + " clients";
        }

        /// <summary>
        /// Whether a restart is needed to use the configured port: the server reads the port only
        /// when it starts (a new address restarts it by itself).
        /// </summary>
        public static bool RestartNeeded(bool listening, int runningPort, int configuredPort)
        {
            return listening && runningPort != configuredPort;
        }

        public static string RestartText(int configuredPort)
        {
            return "Restart telnet to use port " + configuredPort.ToString(CultureInfo.InvariantCulture);
        }
    }
}
