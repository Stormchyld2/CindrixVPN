using System.Text.Json.Serialization;
using CindrixVPN.Common;

namespace CindrixVPN.Models;

/// <summary>One family member's connection: a WireGuard config plus their own settings.</summary>
public sealed class Profile : Observable
{
    private string _name = "Profile";
    private bool _killSwitch = true;
    private bool _netShield;
    private List<string> _excludes = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];

    public string Name { get => _name; set => Set(ref _name, value); }

    public bool KillSwitch { get => _killSwitch; set => Set(ref _killSwitch, value); }

    public bool NetShield { get => _netShield; set => Set(ref _netShield, value); }

    public List<string> Excludes
    {
        get => _excludes;
        set { _excludes = value ?? new(); Raise(nameof(ExcludesText)); }
    }

    /// <summary>The split-tunneling list as editable text, one address or range per line.</summary>
    [JsonIgnore]
    public string ExcludesText
    {
        get => string.Join(Environment.NewLine, _excludes);
        set
        {
            var lines = (value ?? "")
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (lines.SequenceEqual(_excludes)) return;
            _excludes = lines;
            Raise();
        }
    }

    public override string ToString() => Name;
}
