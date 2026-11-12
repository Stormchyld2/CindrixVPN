using System.Net;
using System.Net.Sockets;

namespace CindrixVPN.Services;

public readonly record struct Ipv4Range(uint Start, uint End);

/// <summary>IPv4 helpers for split tunneling: turns "everything except these" into a list of routes.</summary>
public static class Cidr
{
    public static bool TryParse(string text, out Ipv4Range range)
    {
        range = default;
        var parts = text.Trim().Split('/');
        if (parts.Length is < 1 or > 2) return false;
        if (!IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;

        int bits = 32;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out bits) || bits < 0 || bits > 32)) return false;

        byte[] b = ip.GetAddressBytes();
        uint address = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        uint mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
        uint start = address & mask;
        range = new Ipv4Range(start, start | ~mask);
        return true;
    }

    /// <summary>Every IPv4 address NOT covered by the given ranges, as the smallest list of CIDR blocks.</summary>
    public static List<string> Complement(IEnumerable<Ipv4Range> excluded)
    {
        var gaps = new List<(ulong Start, ulong End)>();
        ulong cursor = 0;
        foreach (var r in excluded.OrderBy(x => x.Start))
        {
            if (r.Start > cursor) gaps.Add((cursor, (ulong)r.Start - 1));
            if ((ulong)r.End + 1 > cursor) cursor = (ulong)r.End + 1;
        }
        if (cursor <= uint.MaxValue) gaps.Add((cursor, uint.MaxValue));

        var result = new List<string>();
        foreach (var (start, end) in gaps) AddBlocks(start, end, result);
        return result;
    }

    private static void AddBlocks(ulong start, ulong end, List<string> output)
    {
        while (start <= end)
        {
            ulong size = start == 0 ? (1UL << 32) : (start & (~start + 1UL));
            ulong remaining = end - start + 1;
            while (size > remaining) size >>= 1;
            int bits = 32 - (int)Math.Log2(size);
            output.Add($"{Format(start)}/{bits}");
            start += size;
        }
    }

    private static string Format(ulong v) => $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
}
