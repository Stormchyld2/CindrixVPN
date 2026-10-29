using System.Security.Cryptography;

namespace CindrixVPN.Core;

public static class WireGuardKeys
{
    /// <summary>Creates a new random, clamped WireGuard private key (base64).</summary>
    public static string GeneratePrivateKey()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        key[0] &= 248;
        key[31] &= 127;
        key[31] |= 64;
        return Convert.ToBase64String(key);
    }

    public static string GetPublicKey(string privateKeyBase64)
    {
        byte[] privateKey = Decode(privateKeyBase64);
        return Convert.ToBase64String(X25519.ComputePublicKey(privateKey));
    }

    public static bool IsValidKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(base64.Trim()).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Decode(string base64)
    {
        byte[] key = Convert.FromBase64String(base64.Trim());
        if (key.Length != 32)
        {
            throw new FormatException("A WireGuard key must be exactly 32 bytes.");
        }

        return key;
    }
}
