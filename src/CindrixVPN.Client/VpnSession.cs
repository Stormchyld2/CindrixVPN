using System.Windows.Threading;
using CindrixVPN.Core;

namespace CindrixVPN.Client;

internal enum VpnState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>Shared state for the dashboard, the tray flyout and the tray menu. Used on the UI thread only.</summary>
internal sealed class VpnSession
{
    private readonly TunnelController _tunnel = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _wantConnected;
    private bool _busy;

    public VpnSession()
    {
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public event Action? Changed;

    public AppSettings Settings { get; } = AppSettings.Load();

    public IReadOnlyList<ServerEntry> Servers { get; private set; } = Array.Empty<ServerEntry>();

    public ServerEntry? SelectedServer { get; private set; }

    public string PrivateKey { get; private set; } = string.Empty;

    public string PublicKey { get; private set; } = string.Empty;

    public bool StartWithWindows { get; private set; }

    public bool Ready { get; private set; }

    public VpnState State { get; private set; } = VpnState.Disconnected;

    public string Detail { get; private set; } = "Your traffic is not protected";

    public string StatsText { get; private set; } = string.Empty;

    public string? Error { get; private set; }

    public bool Busy => _busy;

    public bool IsOn => _wantConnected;

    public async Task InitializeAsync()
    {
        try
        {
            PrivateKey = DeviceKeyStore.LoadOrCreatePrivateKey();
            PublicKey = WireGuardKeys.GetPublicKey(PrivateKey);
            Servers = ServerCatalog.Load(AppPaths.ServersFile).Servers;
            SelectedServer = Servers.FirstOrDefault(s => s.Name == Settings.ServerName) ?? Servers[0];
            Ready = true;
        }
        catch (Exception ex)
        {
            Error = "Startup problem: " + ex.Message;
        }

        StartWithWindows = await Task.Run(StartupManager.IsEnabled);

        // Pick up a tunnel that is still running from an earlier session.
        if (Ready && await _tunnel.IsServiceRunningAsync())
        {
            _wantConnected = true;
        }

        _timer.Start();
        await RefreshAsync();

        if (Ready && !_wantConnected && Settings.AutoConnect)
        {
            await ToggleAsync();
        }
    }

    public void SelectServer(ServerEntry server)
    {
        if (_wantConnected || ReferenceEquals(server, SelectedServer))
        {
            return;
        }

        SelectedServer = server;
        Settings.ServerName = server.Name;
        Settings.Save();
        Changed?.Invoke();
    }

    public void SetAutoConnect(bool value)
    {
        Settings.AutoConnect = value;
        Settings.Save();
        Changed?.Invoke();
    }

    public async Task SetStartWithWindowsAsync(bool value)
    {
        try
        {
            await Task.Run(() => StartupManager.SetEnabled(value));
            StartWithWindows = value;
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        Changed?.Invoke();
    }

    public async Task ToggleAsync()
    {
        if (_busy || !Ready)
        {
            return;
        }

        _busy = true;
        Error = null;

        try
        {
            if (_wantConnected)
            {
                SetState(VpnState.Connecting, "Disconnecting…");
                await _tunnel.DisconnectAsync();
                _wantConnected = false;
            }
            else if (SelectedServer is { } server)
            {
                _wantConnected = true;
                SetState(VpnState.Connecting, "Starting the encrypted tunnel");
                await _tunnel.ConnectAsync(PrivateKey, server);
            }
        }
        catch (Exception ex)
        {
            _wantConnected = false;
            Error = ex.Message;
            try
            {
                await _tunnel.DisconnectAsync();
            }
            catch (Exception)
            {
            }
        }
        finally
        {
            _busy = false;
        }

        await RefreshAsync();
    }

    /// <summary>Called when the app exits: never leave a tunnel running with no app to control it.</summary>
    public async Task ShutdownAsync()
    {
        _timer.Stop();
        if (_wantConnected || await _tunnel.IsServiceRunningAsync())
        {
            try
            {
                await _tunnel.DisconnectAsync();
            }
            catch (Exception)
            {
            }
        }

        _wantConnected = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        if (!_wantConnected)
        {
            SetState(VpnState.Disconnected, "Your traffic is not protected");
            StatsText = string.Empty;
            Changed?.Invoke();
            return;
        }

        if (!await _tunnel.IsServiceRunningAsync())
        {
            _wantConnected = false;
            Error = "The tunnel service stopped unexpectedly.";
            SetState(VpnState.Disconnected, "Your traffic is not protected");
            Changed?.Invoke();
            return;
        }

        TunnelStats? stats = await _tunnel.GetStatsAsync();
        bool handshake = stats?.LastHandshake is { } h && DateTimeOffset.UtcNow - h < TimeSpan.FromMinutes(3);
        if (handshake)
        {
            SetState(VpnState.Connected, "Your traffic is routed through the server");
            StatsText = $"↓ {FormatBytes(stats!.RxBytes)}   ↑ {FormatBytes(stats.TxBytes)}";
        }
        else
        {
            SetState(VpnState.Connecting, "Waiting for the server handshake");
            StatsText = string.Empty;
        }

        Changed?.Invoke();
    }

    private void SetState(VpnState state, string detail)
    {
        State = state;
        Detail = detail;
        Changed?.Invoke();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
