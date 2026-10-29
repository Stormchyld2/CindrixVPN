namespace CindrixVPN.Core;

public sealed record TunnelStats(long RxBytes, long TxBytes, DateTimeOffset? LastHandshake);

/// <summary>Parses the key=value text returned by the WireGuard tunnel's status pipe.</summary>
public static class UapiParser
{
    public static TunnelStats Parse(string text)
    {
        long rx = 0;
        long tx = 0;
        long handshakeSeconds = 0;

        foreach (string rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator];
            string value = line[(separator + 1)..];

            if (key == "rx_bytes" && long.TryParse(value, out long rxValue))
            {
                rx += rxValue;
            }
            else if (key == "tx_bytes" && long.TryParse(value, out long txValue))
            {
                tx += txValue;
            }
            else if (key == "last_handshake_time_sec" && long.TryParse(value, out long hsValue))
            {
                handshakeSeconds = Math.Max(handshakeSeconds, hsValue);
            }
        }

        DateTimeOffset? handshake = handshakeSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(handshakeSeconds)
            : null;

        return new TunnelStats(rx, tx, handshake);
    }
}
