using System.IO;

namespace CindrixVPN.Client;

internal static class AppPaths
{
    public const string TunnelName = "cindrixvpn";
    public const string ServiceName = "CindrixVPNTunnel";

    public static string AppDir => AppContext.BaseDirectory;

    public static string TunnelServiceExe => Path.Combine(AppDir, "CindrixVPN.TunnelService.exe");

    /// <summary>Per-user data (device key, optional servers override).</summary>
    public static string UserDir => Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CindrixVPN")).FullName;

    public static string DeviceKeyFile => Path.Combine(UserDir, "device.key");

    public static string ServersFile
    {
        get
        {
            string custom = Path.Combine(UserDir, "servers.json");
            return File.Exists(custom) ? custom : Path.Combine(AppDir, "servers.json");
        }
    }

    /// <summary>Machine-wide runtime folder holding the generated tunnel config (admin/SYSTEM only).</summary>
    public static string RuntimeDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CindrixVPN");

    public static string TunnelConfigFile => Path.Combine(RuntimeDir, TunnelName + ".conf");
}
