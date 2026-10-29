using System.Numerics;

namespace CindrixVPN.Core;

/// <summary>
/// X25519 scalar multiplication (RFC 7748), used to derive a WireGuard public key from a private key.
/// Not constant-time; it is only used locally to display this device's own public key.
/// </summary>
public static class X25519
{
    private static readonly BigInteger P = (BigInteger.One << 255) - 19;
    private static readonly BigInteger A24 = new(121665);

    public static byte[] BasePoint { get; } = CreateBasePoint();

    private static byte[] CreateBasePoint()
    {
        byte[] b = new byte[32];
        b[0] = 9;
        return b;
    }

    public static byte[] ComputePublicKey(byte[] privateKey) => ScalarMult(privateKey, BasePoint);

    public static byte[] ScalarMult(byte[] scalar, byte[] u)
    {
        if (scalar.Length != 32)
        {
            throw new ArgumentException("Scalar must be 32 bytes.", nameof(scalar));
        }

        if (u.Length != 32)
        {
            throw new ArgumentException("Point must be 32 bytes.", nameof(u));
        }

        byte[] k = (byte[])scalar.Clone();
        k[0] &= 248;
        k[31] &= 127;
        k[31] |= 64;

        byte[] uBytes = (byte[])u.Clone();
        uBytes[31] &= 127;

        BigInteger kInt = new(k, isUnsigned: true, isBigEndian: false);
        BigInteger x1 = new BigInteger(uBytes, isUnsigned: true, isBigEndian: false) % P;
        BigInteger x2 = BigInteger.One;
        BigInteger z2 = BigInteger.Zero;
        BigInteger x3 = x1;
        BigInteger z3 = BigInteger.One;
        bool swap = false;

        for (int t = 254; t >= 0; t--)
        {
            bool bit = !((kInt >> t) & BigInteger.One).IsZero;
            if (swap != bit)
            {
                (x2, x3) = (x3, x2);
                (z2, z3) = (z3, z2);
            }

            swap = bit;

            BigInteger a = Mod(x2 + z2);
            BigInteger aa = Mod(a * a);
            BigInteger b = Mod(x2 - z2);
            BigInteger bb = Mod(b * b);
            BigInteger e = Mod(aa - bb);
            BigInteger c = Mod(x3 + z3);
            BigInteger d = Mod(x3 - z3);
            BigInteger da = Mod(d * a);
            BigInteger cb = Mod(c * b);
            BigInteger sum = Mod(da + cb);
            BigInteger diff = Mod(da - cb);

            x3 = Mod(sum * sum);
            z3 = Mod(x1 * Mod(diff * diff));
            x2 = Mod(aa * bb);
            z2 = Mod(e * Mod(aa + A24 * e));
        }

        if (swap)
        {
            (x2, x3) = (x3, x2);
            (z2, z3) = (z3, z2);
        }

        BigInteger result = Mod(x2 * BigInteger.ModPow(z2, P - 2, P));
        byte[] raw = result.ToByteArray(isUnsigned: true, isBigEndian: false);
        byte[] output = new byte[32];
        Array.Copy(raw, output, Math.Min(raw.Length, 32));
        return output;
    }

    private static BigInteger Mod(BigInteger value)
    {
        BigInteger r = value % P;
        return r.Sign < 0 ? r + P : r;
    }
}
