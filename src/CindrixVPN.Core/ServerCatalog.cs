using System.Text.Json;

namespace CindrixVPN.Core;

public sealed class ServerEntry
{
    public string Name { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public int Port { get; set; } = 51820;

    public string ServerPublicKey { get; set; } = string.Empty;

    public string ClientAddress { get; set; } = string.Empty;

    public List<string> Dns { get; set; } = new();

    public override string ToString() => Name;

    public void Validate()
    {
        RequireText(Name, nameof(Name));
        RequireText(Endpoint, nameof(Endpoint));
        RequireText(ClientAddress, nameof(ClientAddress));

        if (Port is < 1 or > 65535)
        {
            throw new FormatException($"Server '{Name}': port must be between 1 and 65535.");
        }

        if (!WireGuardKeys.IsValidKey(ServerPublicKey))
        {
            throw new FormatException($"Server '{Name}': serverPublicKey is not a valid WireGuard key.");
        }

        foreach (string dns in Dns)
        {
            RequireText(dns, "Dns");
        }
    }

    private void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Server '{Name}': {field} is required.");
        }

        // Values are written into a config file, so line breaks and control characters are not allowed.
        if (value.Any(char.IsControl))
        {
            throw new FormatException($"Server '{Name}': {field} contains invalid characters.");
        }
    }
}

public sealed class ServerCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<ServerEntry> Servers { get; set; } = new();

    public static ServerCatalog Parse(string json)
    {
        ServerCatalog? catalog = JsonSerializer.Deserialize<ServerCatalog>(json, Options);
        if (catalog is null || catalog.Servers.Count == 0)
        {
            throw new FormatException("The server list is empty.");
        }

        foreach (ServerEntry server in catalog.Servers)
        {
            server.Validate();
        }

        return catalog;
    }

    public static ServerCatalog Load(string path) => Parse(File.ReadAllText(path));
}
