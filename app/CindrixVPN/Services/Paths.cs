using System.Diagnostics;

namespace CindrixVPN.Services;

public static class Paths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CindrixVPN");

    public static string Profiles => Path.Combine(Root, "profiles");
    public static string Tunnels => Path.Combine(Root, "tunnels");
    public static string Logs => Path.Combine(Root, "logs");
    public static string LogFile => Path.Combine(Logs, "cindrix.log");

    public static string ProfileConf(string id) => Path.Combine(Profiles, id + ".conf");

    /// <summary>Creates the data folders and limits them to SYSTEM and Administrators (configs hold private keys).</summary>
    public static void EnsureSecure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Profiles);
        Directory.CreateDirectory(Tunnels);
        Directory.CreateDirectory(Logs);
        try
        {
            var psi = new ProcessStartInfo("icacls.exe",
                $"\"{Root}\" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F /T /C /Q")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.StandardOutput.ReadToEnd();
            p?.WaitForExit(10000);
        }
        catch (Exception ex)
        {
            Log.Error("Could not restrict the data folder: " + ex.Message);
        }
    }
}
