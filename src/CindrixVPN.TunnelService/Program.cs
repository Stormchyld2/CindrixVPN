using System.Runtime.InteropServices;

namespace CindrixVPN.TunnelService;

/// <summary>
/// Entry point of the Windows service that hosts the embedded WireGuard tunnel.
/// tunnel.dll runs the service dispatcher itself, so this only passes it the config path.
/// Usage: CindrixVPN.TunnelService.exe "C:\path\cindrixvpn.conf"
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            return 2;
        }

        return RunTunnel(args[0]) ? 0 : 1;
    }

    [DllImport("tunnel.dll", EntryPoint = "WireGuardTunnelService", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool RunTunnel([MarshalAs(UnmanagedType.LPWStr)] string configFile);
}
