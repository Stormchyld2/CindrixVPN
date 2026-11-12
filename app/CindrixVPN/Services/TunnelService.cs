using System.Diagnostics;

namespace CindrixVPN.Services;

public sealed record WgSample(long HandshakeUnix, long Rx, long Tx);

/// <summary>Starts and stops tunnels through the official WireGuard for Windows (wireguard.exe / wg.exe).</summary>
public sealed class TunnelService
{
    private static string WireGuardDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard");

    private static string WireGuardExe => Path.Combine(WireGuardDir, "wireguard.exe");
    private static string WgExe => Path.Combine(WireGuardDir, "wg.exe");

    public bool IsInstalled => File.Exists(WireGuardExe) && File.Exists(WgExe);

    public static string TunnelName(string profileId) => "cindrix" + profileId;

    public void Connect(string name, string configText)
    {
        if (!IsInstalled) throw new InvalidOperationException("WireGuard for Windows is not installed.");

        Disconnect(name);
        Directory.CreateDirectory(Paths.Tunnels);
        string path = Path.Combine(Paths.Tunnels, name + ".conf");
        File.WriteAllText(path, configText);

        var (code, output) = Run(WireGuardExe, $"/installtunnelservice \"{path}\"");
        if (code != 0)
            throw new InvalidOperationException("WireGuard could not start the tunnel. " + output.Trim());
    }

    public void Disconnect(string name)
    {
        if (File.Exists(WireGuardExe))
            Run(WireGuardExe, $"/uninstalltunnelservice {name}");
        try
        {
            File.Delete(Path.Combine(Paths.Tunnels, name + ".conf"));
        }
        catch
        {
            // The service may still hold the file for a moment; it is removed on the next connect.
        }
    }

    /// <summary>Removes every Cindrix tunnel service (used if something is stuck).</summary>
    public int DisconnectAll()
    {
        int count = 0;
        if (!Directory.Exists(Paths.Tunnels)) return 0;
        foreach (string file in Directory.GetFiles(Paths.Tunnels, "cindrix*.conf"))
        {
            Disconnect(Path.GetFileNameWithoutExtension(file));
            count++;
        }
        return count;
    }

    public bool IsRunning(string name)
    {
        var (_, output) = Run("sc.exe", $"query WireGuardTunnel${name}");
        return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
            || output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Latest handshake and byte counters, or null if the tunnel interface is not up.</summary>
    public WgSample? Sample(string name)
    {
        if (!File.Exists(WgExe)) return null;
        var (code, output) = Run(WgExe, $"show {name} dump");
        if (code != 0) return null;

        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return new WgSample(0, 0, 0);

        string[] f = lines[1].TrimEnd('\r').Split('\t');
        if (f.Length < 7) return new WgSample(0, 0, 0);
        long.TryParse(f[4], out long handshake);
        long.TryParse(f[5], out long rx);
        long.TryParse(f[6], out long tx);
        return new WgSample(handshake, rx, tx);
    }

    public static (int Code, string Output) Run(string file, string arguments, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + file);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(); } catch { /* already gone */ }
        }
        return (process.HasExited ? process.ExitCode : -1, output);
    }
}
