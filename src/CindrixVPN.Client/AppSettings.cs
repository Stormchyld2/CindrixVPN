using System.IO;
using System.Text.Json;

namespace CindrixVPN.Client;

internal sealed class AppSettings
{
    public bool AutoConnect { get; set; }

    public string? ServerName { get; set; }

    private static string FilePath => Path.Combine(AppPaths.UserDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt settings fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception)
        {
        }
    }
}
