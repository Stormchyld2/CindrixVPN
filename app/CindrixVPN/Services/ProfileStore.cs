using System.Text.Json;
using CindrixVPN.Models;

namespace CindrixVPN.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(Paths.Root, "profiles.json");

    public List<Profile> Profiles { get; set; } = new();
    public string? SelectedId { get; set; }
    public bool AutoConnect { get; set; }

    public static ProfileStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(FilePath)) ?? new ProfileStore();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read profiles: " + ex.Message);
        }
        return new ProfileStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save profiles: " + ex.Message);
        }
    }
}
