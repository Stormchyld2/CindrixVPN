using CindrixVPN.Core;
using Xunit;

namespace CindrixVPN.Core.Tests;

public class CoreTests
{
    private const string ServerKey = "ji6C3LPeC00BIhtSBxU+kYpr5RdfrZs4AKk5guNnEGw=";

    private static byte[] Hex(string h) => Convert.FromHexString(h);

    [Fact]
    public void X25519_Rfc7748_Vector1()
    {
        byte[] result = X25519.ScalarMult(
            Hex("a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4"),
            Hex("e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c"));
        Assert.Equal("c3da55379de9c6908e94ea4df28d084f32eccf03491c71f754b4075577a28552",
            Convert.ToHexString(result).ToLowerInvariant());
    }

    [Fact]
    public void X25519_Rfc7748_AlicePublicKey()
    {
        byte[] pub = X25519.ComputePublicKey(
            Hex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a"));
        Assert.Equal("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a",
            Convert.ToHexString(pub).ToLowerInvariant());
    }

    [Fact]
    public void GeneratedKeys_AreValidAndDerivePublicKey()
    {
        string priv = WireGuardKeys.GeneratePrivateKey();
        Assert.True(WireGuardKeys.IsValidKey(priv));
        Assert.True(WireGuardKeys.IsValidKey(WireGuardKeys.GetPublicKey(priv)));
    }

    [Fact]
    public void IsValidKey_RejectsGarbage()
    {
        Assert.False(WireGuardKeys.IsValidKey(null));
        Assert.False(WireGuardKeys.IsValidKey("not base64!"));
        Assert.False(WireGuardKeys.IsValidKey("AAAA"));
    }

    private const string Json = """
        {"servers":[{"name":"Test","country":"US","endpoint":"157.151.152.154","port":51820,
        "serverPublicKey":"ji6C3LPeC00BIhtSBxU+kYpr5RdfrZs4AKk5guNnEGw=",
        "clientAddress":"10.66.66.2/32","dns":["1.1.1.1"]}]}
        """;

    [Fact]
    public void Catalog_Parses()
    {
        ServerCatalog c = ServerCatalog.Parse(Json);
        Assert.Single(c.Servers);
        Assert.Equal(51820, c.Servers[0].Port);
    }

    [Fact]
    public void Catalog_RejectsBadKey()
    {
        Assert.Throws<FormatException>(() => ServerCatalog.Parse(Json.Replace(ServerKey, "AAAA")));
    }

    [Fact]
    public void Catalog_RejectsInjectedNewline()
    {
        Assert.Throws<FormatException>(() => ServerCatalog.Parse(Json.Replace("157.151.152.154", "1.2.3.4\\nPostUp = calc")));
    }

    [Fact]
    public void ConfigBuilder_ProducesFullTunnelConfig()
    {
        ServerEntry s = ServerCatalog.Parse(Json).Servers[0];
        string conf = WireGuardConfigBuilder.Build(WireGuardKeys.GeneratePrivateKey(), s);
        Assert.Contains("Address = 10.66.66.2/32", conf);
        Assert.Contains("DNS = 1.1.1.1", conf);
        Assert.Contains("AllowedIPs = 0.0.0.0/0, ::/0", conf);
        Assert.Contains("Endpoint = 157.151.152.154:51820", conf);
        Assert.Contains($"PublicKey = {ServerKey}", conf);
    }

    [Fact]
    public void Uapi_ParsesStats()
    {
        TunnelStats st = UapiParser.Parse("public_key=abc\nrx_bytes=100\ntx_bytes=50\nlast_handshake_time_sec=1700000000\nerrno=0\n");
        Assert.Equal(100, st.RxBytes);
        Assert.Equal(50, st.TxBytes);
        Assert.NotNull(st.LastHandshake);
    }

    [Fact]
    public void Uapi_NoHandshake()
    {
        Assert.Null(UapiParser.Parse("rx_bytes=0\ntx_bytes=0\nlast_handshake_time_sec=0\n").LastHandshake);
    }
}
