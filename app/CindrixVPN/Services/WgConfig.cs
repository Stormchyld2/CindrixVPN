using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CindrixVPN.Services;

/// <summary>The parts of a WireGuard client config that this app needs, and a renderer for the final tunnel config.</summary>
public sealed class WgConfig
{
    public string PrivateKey { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string Dns { get; private set; } = "";
    public string PublicKey { get; private set; } = "";
    public string PresharedKey { get; private set; } = "";
    public string Endpoint { get; private set; } = "";

    public string EndpointHost
    {
        get
        {
            int i = Endpoint.LastIndexOf(':');
            return (i > 0 ? Endpoint[..i] : Endpoint).Trim('[', ']', ' ');
        }
    }

    /// <summary>The server's address inside the tunnel (the .1 of the client's subnet).</summary>
    public string Gateway
    {
        get
        {
            string first = Address.Split(',')[0].Trim().Split('/')[0];
            string[] p = first.Split('.');
            return p.Length == 4 ? $"{p[0]}.{p[1]}.{p[2]}.1" : "10.66.66.1";
        }
    }

    public static WgConfig Parse(string text)
    {
        var c = new WgConfig();
        string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('['))
            {
                section = line.Trim('[', ']').Trim().ToLowerInvariant();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim();

            if (section == "interface")
            {
                if (key == "privatekey") c.PrivateKey = value;
                else if (key == "address") c.Address = value;
                else if (key == "dns") c.Dns = value;
            }
            else if (section == "peer")
            {
                if (key == "publickey") c.PublicKey = value;
                else if (key == "presharedkey") c.PresharedKey = value;
                else if (key == "endpoint") c.Endpoint = value;
            }
        }

        if (c.PrivateKey.Length == 0) throw new FormatException("This config has no PrivateKey.");
        if (c.Address.Length == 0) throw new FormatException("This config has no Address.");
        if (c.PublicKey.Length == 0) throw new FormatException("This config has no server PublicKey.");
        if (c.Endpoint.Length == 0) throw new FormatException("This config has no server Endpoint.");
        return c;
    }

    /// <summary>
    /// Builds the tunnel config. WireGuard for Windows turns on its firewall block ("kill switch") only when
    /// AllowedIPs is exactly the default route (0.0.0.0/0, ::/0); the /1 pairs route everything without blocking.
    /// </summary>
    public string Render(bool killSwitch, bool netShield, IEnumerable<string> excludes, out bool killSwitchActive)
    {
        var ranges = new List<Ipv4Range>();
        foreach (string e in excludes)
            if (Cidr.TryParse(e, out var r)) ranges.Add(r);

        string allowed;
        if (ranges.Count > 0)
        {
            // Keep the server itself reachable outside the tunnel, then route everything else through it.
            if (IPAddress.TryParse(EndpointHost, out var ep) && ep.AddressFamily == AddressFamily.InterNetwork
                && Cidr.TryParse(EndpointHost, out var serverRange))
                ranges.Add(serverRange);
            allowed = string.Join(", ", Cidr.Complement(ranges).Concat(new[] { "::/1", "8000::/1" }));
            killSwitchActive = false;
        }
        else if (killSwitch)
        {
            allowed = "0.0.0.0/0, ::/0";
            killSwitchActive = true;
        }
        else
        {
            allowed = "0.0.0.0/1, 128.0.0.0/1, ::/1, 8000::/1";
            killSwitchActive = false;
        }

        string dns = netShield ? Gateway : (Dns.Length > 0 ? Dns : "1.1.1.1");

        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {PrivateKey}");
        sb.AppendLine($"Address = {Address}");
        sb.AppendLine($"DNS = {dns}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {PublicKey}");
        if (PresharedKey.Length > 0) sb.AppendLine($"PresharedKey = {PresharedKey}");
        sb.AppendLine($"AllowedIPs = {allowed}");
        sb.AppendLine($"Endpoint = {Endpoint}");
        sb.AppendLine("PersistentKeepalive = 25");
        return sb.ToString();
    }
}
