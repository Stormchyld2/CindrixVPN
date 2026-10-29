using System.Text;

namespace CindrixVPN.Core;

public static class WireGuardConfigBuilder
{
    /// <summary>
    /// Builds a full-tunnel WireGuard config. With AllowedIPs covering everything, the WireGuard tunnel
    /// service also blocks traffic that would bypass the tunnel (its built-in kill switch behavior).
    /// </summary>
    public static string Build(string privateKey, ServerEntry server)
    {
        if (!WireGuardKeys.IsValidKey(privateKey))
        {
            throw new FormatException("The device private key is not valid.");
        }

        server.Validate();

        StringBuilder sb = new();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {privateKey.Trim()}");
        sb.AppendLine($"Address = {server.ClientAddress}");
        if (server.Dns.Count > 0)
        {
            sb.AppendLine($"DNS = {string.Join(", ", server.Dns)}");
        }

        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {server.ServerPublicKey}");
        sb.AppendLine("AllowedIPs = 0.0.0.0/0, ::/0");
        sb.AppendLine($"Endpoint = {server.Endpoint}:{server.Port}");
        sb.AppendLine("PersistentKeepalive = 25");
        return sb.ToString();
    }
}
