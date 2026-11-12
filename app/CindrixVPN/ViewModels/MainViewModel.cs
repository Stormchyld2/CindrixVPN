using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CindrixVPN.Common;
using CindrixVPN.Models;
using CindrixVPN.Services;

namespace CindrixVPN.ViewModels;

public enum VpnState { Disconnected, Connecting, Connected, Reconnecting }

public sealed class MainViewModel : Observable
{
    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush Green = Frozen(0x34, 0xE2, 0x7A);
    private static readonly Brush Red = Frozen(0xFF, 0x4D, 0x5E);
    private static readonly Brush Amber = Frozen(0xFF, 0xB0, 0x20);

    private const int SparkPoints = 60;
    private const double SparkWidth = 300, SparkHeight = 70;

    private readonly ProfileStore _store;
    private readonly TunnelService _tunnel = new();
    private readonly StatsClient _stats = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<double> _downHistory = new();
    private readonly Queue<double> _upHistory = new();

    private Profile? _selected;
    private Profile? _connectedProfile;
    private WgConfig? _config;
    private bool _killActive;
    private bool _busy;
    private bool _ticking;
    private int _tick;
    private DateTime _connectingSince;
    private DateTime? _connectedAt;
    private long _lastRx, _lastTx;
    private DateTime _lastSample;
    private CancellationTokenSource? _reapply;

    private VpnState _state = VpnState.Disconnected;
    private string _duration = "00:00:00";
    private string _downTotal = "0 B", _upTotal = "0 B";
    private string _downSpeed = "0 KB/s", _upSpeed = "0 KB/s";
    private string _latency = "—";
    private string _serverLoadText = "—";
    private double _serverLoad;
    private long _blocked;
    private PointCollection _speedLine = new();
    private PointCollection _speedArea = new();
    private PointCollection _speedLineUp = new();

    public MainViewModel()
    {
        _store = ProfileStore.Load();
        foreach (var p in _store.Profiles)
        {
            Profiles.Add(p);
            Hook(p);
        }
        _selected = Profiles.FirstOrDefault(p => p.Id == _store.SelectedId) ?? Profiles.FirstOrDefault();

        foreach (string line in Log.Recent(200)) Logs.Add(line);
        Log.Line += line => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Logs.Add(line);
            while (Logs.Count > 500) Logs.RemoveAt(0);
        });

        ToggleCommand = new RelayCommand(ToggleAsync);
        _timer.Tick += async (_, _) => await TickAsync();
        UpdateSpark();
        RefreshProtection();
    }

    // ---------------------------------------------------------------- collections & events

    public ObservableCollection<Profile> Profiles { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public event Action<string>? Message;
    public event Action<string>? NavigateRequested;

    public RelayCommand ToggleCommand { get; }

    public bool WireGuardInstalled => _tunnel.IsInstalled;

    public bool AutoConnect
    {
        get => _store.AutoConnect;
        set { _store.AutoConnect = value; _store.Save(); Raise(); }
    }

    public Profile? SelectedProfile
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            _store.SelectedId = value?.Id;
            _store.Save();
            RaiseSettings();
        }
    }

    // ---------------------------------------------------------------- state shown on the dashboard

    public VpnState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(IsConnected));
            Raise(nameof(StatusText));
            Raise(nameof(StatusSubtitle));
            Raise(nameof(StatusBrush));
            Raise(nameof(ConnectButtonText));
            Raise(nameof(ServerBadge));
            Raise(nameof(ProtectionText));
            Raise(nameof(ProtectionBrush));
            RefreshProtection();
        }
    }

    public bool IsConnected => State == VpnState.Connected;

    public string StatusText => State switch
    {
        VpnState.Connected => "Connected",
        VpnState.Connecting => "Connecting…",
        VpnState.Reconnecting => "Reconnecting…",
        _ => "Disconnected"
    };

    public string StatusSubtitle => State switch
    {
        VpnState.Connected => "Your traffic is encrypted and protected",
        VpnState.Connecting or VpnState.Reconnecting => "Establishing a secure tunnel",
        _ => "Your traffic is not protected"
    };

    public Brush StatusBrush => State switch
    {
        VpnState.Connected => Green,
        VpnState.Connecting or VpnState.Reconnecting => Amber,
        _ => Red
    };

    /// <summary>The one button in the middle: Connect when off, Disconnect when on.</summary>
    public string ConnectButtonText => State switch
    {
        VpnState.Disconnected => "Connect",
        VpnState.Connecting => "Connecting… (click to cancel)",
        _ => "Disconnect"
    };

    public string ServerBadge => State switch
    {
        VpnState.Connected => "Online",
        VpnState.Connecting or VpnState.Reconnecting => "Connecting",
        _ => "Ready"
    };

    public string ServerNameText => "CindrixVPN Server";
    public string EncryptionText => "ChaCha20";
    public string ProtocolText => "WireGuard®";

    public string ProtectionText => IsConnected ? "Active" : "Inactive";
    public Brush ProtectionBrush => IsConnected ? Green : Red;

    public string ExitIpText => IsConnected && _config != null ? _config.EndpointHost : "Not protected";

    public string DurationText { get => _duration; private set => Set(ref _duration, value); }
    public string DownTotalText { get => _downTotal; private set => Set(ref _downTotal, value); }
    public string UpTotalText { get => _upTotal; private set => Set(ref _upTotal, value); }
    public string DownSpeedText { get => _downSpeed; private set => Set(ref _downSpeed, value); }
    public string UpSpeedText { get => _upSpeed; private set => Set(ref _upSpeed, value); }
    public string LatencyText { get => _latency; private set => Set(ref _latency, value); }
    public string ServerLoadText { get => _serverLoadText; private set => Set(ref _serverLoadText, value); }
    public double ServerLoad { get => _serverLoad; private set => Set(ref _serverLoad, value); }
    public PointCollection SpeedLine { get => _speedLine; private set => Set(ref _speedLine, value); }
    public PointCollection SpeedArea { get => _speedArea; private set => Set(ref _speedArea, value); }
    public PointCollection SpeedLineUp { get => _speedLineUp; private set => Set(ref _speedLineUp, value); }

    // ---------------------------------------------------------------- feature tiles

    public string NetShieldState => SelectedProfile?.NetShield == true ? "ON" : "OFF";
    public string KillSwitchState => SelectedProfile == null || !SelectedProfile.KillSwitch ? "OFF"
        : SelectedProfile.Excludes.Count > 0 ? "PAUSED" : "ON";
    public string SplitState => SelectedProfile is { Excludes.Count: > 0 } p ? $"{p.Excludes.Count} RULE{(p.Excludes.Count == 1 ? "" : "S")}" : "OFF";
    public string ProfileName => SelectedProfile?.Name ?? "No profile";

    private void RaiseSettings()
    {
        Raise(nameof(NetShieldState));
        Raise(nameof(KillSwitchState));
        Raise(nameof(SplitState));
        Raise(nameof(ProfileName));
        RefreshProtection();
    }

    // ---------------------------------------------------------------- protection checklist

    public string TunnelText { get; private set; } = "";
    public string KillText { get; private set; } = "";
    public string NetShieldText { get; private set; } = "";
    public string DnsText { get; private set; } = "";
    public string Ipv6Text { get; private set; } = "";
    public bool TunnelOk { get; private set; }
    public bool KillOk { get; private set; }
    public bool NetShieldOk { get; private set; }
    public bool DnsOk { get; private set; }
    public bool Ipv6Ok { get; private set; }

    private void RefreshProtection()
    {
        bool up = IsConnected;
        var p = _connectedProfile ?? SelectedProfile;
        bool shield = p?.NetShield == true;

        TunnelOk = up;
        TunnelText = up ? "VPN Tunnel Active" : "VPN Tunnel Inactive";

        KillOk = up && _killActive;
        KillText = KillOk ? "Kill Switch Enabled"
            : p is { KillSwitch: true, Excludes.Count: > 0 } ? "Kill Switch Paused (split tunneling)"
            : "Kill Switch Disabled";

        NetShieldOk = up && shield;
        NetShieldText = !shield ? "NetShield Off"
            : _blocked > 0 ? $"NetShield Blocking ({_blocked:N0} items)"
            : "NetShield On";

        DnsOk = up && (_killActive || shield);
        DnsText = DnsOk ? "DNS Leak Protection" : "DNS Leak Protection Off";

        Ipv6Ok = up;
        Ipv6Text = up ? "IPv6 Leak Protection" : "IPv6 Leak Protection Off";

        foreach (string n in new[]
        {
            nameof(TunnelText), nameof(KillText), nameof(NetShieldText), nameof(DnsText), nameof(Ipv6Text),
            nameof(TunnelOk), nameof(KillOk), nameof(NetShieldOk), nameof(DnsOk), nameof(Ipv6Ok), nameof(ExitIpText)
        })
            Raise(n);
    }

    // ---------------------------------------------------------------- profiles

    private void Hook(Profile p)
    {
        p.PropertyChanged += (_, e) =>
        {
            _store.Save();
            if (p != SelectedProfile) return;
            RaiseSettings();
            if (e.PropertyName is nameof(Profile.KillSwitch) or nameof(Profile.NetShield) or nameof(Profile.ExcludesText))
                ScheduleReapply(p);
        };
    }

    public void AddProfile(string suggestedName, string configText)
    {
        WgConfig.Parse(configText); // throws FormatException with a readable message if the file is not usable
        var profile = new Profile { Name = suggestedName };
        Directory.CreateDirectory(Paths.Profiles);
        File.WriteAllText(Paths.ProfileConf(profile.Id), configText);
        _store.Profiles.Add(profile);
        Profiles.Add(profile);
        Hook(profile);
        _store.Save();
        SelectedProfile = profile;
        Log.Info($"Added profile '{profile.Name}'.");
    }

    public async Task RemoveProfileAsync(Profile profile)
    {
        if (_connectedProfile == profile) await DisconnectAsync();
        try { File.Delete(Paths.ProfileConf(profile.Id)); } catch { /* ignore */ }
        _store.Profiles.Remove(profile);
        Profiles.Remove(profile);
        if (SelectedProfile == profile) SelectedProfile = Profiles.FirstOrDefault();
        _store.Save();
        Log.Info($"Removed profile '{profile.Name}'.");
    }

    // ---------------------------------------------------------------- connect / disconnect

    public void Start()
    {
        _timer.Start();
        _ = Task.Run(async () =>
        {
            var running = Profiles.FirstOrDefault(p => _tunnel.IsRunning(TunnelService.TunnelName(p.Id)));
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (running != null)
                {
                    await AdoptAsync(running);
                }
                else if (AutoConnect && SelectedProfile != null)
                {
                    await ConnectAsync();
                }
            }).Task.Unwrap();
        });
    }

    private async Task AdoptAsync(Profile p)
    {
        try
        {
            _config = WgConfig.Parse(await File.ReadAllTextAsync(Paths.ProfileConf(p.Id)));
            _killActive = p.KillSwitch && p.Excludes.Count == 0;
            _connectedProfile = p;
            _connectingSince = DateTime.UtcNow;
            SelectedProfile = p;
            State = VpnState.Connecting;
            Log.Info($"Found a running tunnel for '{p.Name}'.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not attach to the running tunnel: " + ex.Message);
        }
    }

    public async Task ToggleAsync()
    {
        if (_busy) return;
        if (State == VpnState.Disconnected) await ConnectAsync();
        else await DisconnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (_busy || State != VpnState.Disconnected) return;

        var p = SelectedProfile;
        if (p == null)
        {
            Message?.Invoke("Add a profile first: import the .conf file that the server made for this device.");
            NavigateRequested?.Invoke("Profiles");
            return;
        }
        if (!_tunnel.IsInstalled)
        {
            Message?.Invoke("WireGuard for Windows is required. Install it from wireguard.com/install, then press Connect again.");
            return;
        }

        _busy = true;
        try
        {
            var cfg = WgConfig.Parse(await File.ReadAllTextAsync(Paths.ProfileConf(p.Id)));
            string rendered = cfg.Render(p.KillSwitch, p.NetShield, p.Excludes, out bool killActive);

            _config = cfg;
            _killActive = killActive;
            _connectedProfile = p;
            _connectedAt = null;
            _connectingSince = DateTime.UtcNow;
            ResetCounters();
            State = VpnState.Connecting;
            Log.Info($"Connecting '{p.Name}' to {cfg.Endpoint} (kill switch {(killActive ? "on" : "off")}, NetShield {(p.NetShield ? "on" : "off")}).");

            string name = TunnelService.TunnelName(p.Id);
            await Task.Run(() => _tunnel.Connect(name, rendered));
        }
        catch (Exception ex)
        {
            Log.Error("Connect failed: " + ex.Message);
            _connectedProfile = null;
            State = VpnState.Disconnected;
            Message?.Invoke("Could not connect. " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var p = _connectedProfile ?? SelectedProfile;
            if (p != null)
            {
                string name = TunnelService.TunnelName(p.Id);
                await Task.Run(() => _tunnel.Disconnect(name));
                Log.Info($"Disconnected '{p.Name}'.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Disconnect failed: " + ex.Message);
        }
        finally
        {
            _connectedProfile = null;
            _connectedAt = null;
            ResetCounters();
            State = VpnState.Disconnected;
            _busy = false;
        }
    }

    public async Task<int> ResetAllTunnelsAsync()
    {
        int count = await Task.Run(() => _tunnel.DisconnectAll());
        _connectedProfile = null;
        State = VpnState.Disconnected;
        ResetCounters();
        Log.Info($"Removed {count} tunnel(s).");
        return count;
    }

    private void ScheduleReapply(Profile p)
    {
        if (_connectedProfile != p || State == VpnState.Disconnected) return;
        _reapply?.Cancel();
        var cts = _reapply = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(700, cts.Token); } catch (TaskCanceledException) { return; }
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                Log.Info("Settings changed while connected; reconnecting to apply them.");
                await DisconnectAsync();
                await ConnectAsync();
            }).Task.Unwrap();
        });
    }

    // ---------------------------------------------------------------- once-a-second polling

    private async Task TickAsync()
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            await PollAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Status check failed: " + ex.Message);
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task PollAsync()
    {
        var p = _connectedProfile;
        if (p == null || _busy) return;

        string name = TunnelService.TunnelName(p.Id);
        var (running, sample) = await Task.Run(() => (_tunnel.IsRunning(name), _tunnel.Sample(name)));
        double sinceStart = (DateTime.UtcNow - _connectingSince).TotalSeconds;

        if (!running)
        {
            if (State == VpnState.Connecting && sinceStart < 10) return; // service is still starting
            Log.Info("The tunnel service stopped.");
            _connectedProfile = null;
            _connectedAt = null;
            ResetCounters();
            State = VpnState.Disconnected;
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool fresh = sample != null && sample.HandshakeUnix > 0 && now - sample.HandshakeUnix < 180;

        if (fresh)
        {
            if (State != VpnState.Connected)
            {
                _connectedAt ??= DateTime.UtcNow;
                State = VpnState.Connected;
                Log.Info("Connected: handshake with the server completed.");
                _ = RefreshRemoteAsync();
            }
        }
        else if (State == VpnState.Connected)
        {
            State = VpnState.Reconnecting;
            Log.Info("No recent handshake; waiting for the server.");
        }
        else if (State == VpnState.Connecting && sinceStart > 20)
        {
            Log.Error("No answer from the server after 20 seconds; disconnecting.");
            await DisconnectAsync();
            Message?.Invoke("The server did not answer. Check that UDP port 51820 is open in Oracle and that this device's config was added on the server.");
            return;
        }

        if (sample != null) UpdateTraffic(sample);

        if (_connectedAt is DateTime since)
            DurationText = (DateTime.UtcNow - since).ToString(@"hh\:mm\:ss");

        if (IsConnected && ++_tick % 5 == 0) _ = RefreshRemoteAsync();
    }

    private void UpdateTraffic(WgSample s)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastSample).TotalSeconds;
        double down = 0, up = 0;
        if (_lastSample != default && dt > 0.2 && s.Rx >= _lastRx && s.Tx >= _lastTx)
        {
            down = (s.Rx - _lastRx) / dt;
            up = (s.Tx - _lastTx) / dt;
        }
        _lastRx = s.Rx;
        _lastTx = s.Tx;
        _lastSample = now;

        DownTotalText = FormatBytes(s.Rx);
        UpTotalText = FormatBytes(s.Tx);
        DownSpeedText = FormatSpeed(down);
        UpSpeedText = FormatSpeed(up);

        _downHistory.Enqueue(down);
        _upHistory.Enqueue(up);
        while (_downHistory.Count > SparkPoints) _downHistory.Dequeue();
        while (_upHistory.Count > SparkPoints) _upHistory.Dequeue();
        UpdateSpark();
    }

    private async Task RefreshRemoteAsync()
    {
        var cfg = _config;
        if (cfg == null) return;

        long? ms = await PingAsync(cfg.Gateway) ?? await PingAsync("1.1.1.1");
        LatencyText = ms is long v ? $"{v} ms" : "—";

        var stats = await _stats.GetAsync(cfg.Gateway);
        if (stats != null)
        {
            ServerLoad = Math.Clamp(stats.Load, 0, 100);
            ServerLoadText = $"{stats.Load}%";
            _blocked = stats.Blocked;
        }
        else
        {
            ServerLoad = 0;
            ServerLoadText = "—";
        }
        RefreshProtection();
    }

    private static async Task<long?> PingAsync(string address)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(IPAddress.Parse(address), 1000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private void ResetCounters()
    {
        _lastRx = _lastTx = 0;
        _lastSample = default;
        _downHistory.Clear();
        _upHistory.Clear();
        _blocked = 0;
        _tick = 0;
        DurationText = "00:00:00";
        DownTotalText = UpTotalText = "0 B";
        DownSpeedText = UpSpeedText = "0 KB/s";
        LatencyText = "—";
        ServerLoadText = "—";
        ServerLoad = 0;
        UpdateSpark();
    }

    /// <summary>Redraws the last minute of traffic: download as a filled line, upload as a thinner line, one shared scale.</summary>
    private void UpdateSpark()
    {
        double peak = 50_000; // never zoom in on noise: the scale floor is about 50 KB/s
        if (_downHistory.Count > 0) peak = Math.Max(peak, _downHistory.Max());
        if (_upHistory.Count > 0) peak = Math.Max(peak, _upHistory.Max());

        var down = BuildLine(_downHistory, peak);
        var up = BuildLine(_upHistory, peak);

        var area = new PointCollection { new Point(0, SparkHeight) };
        foreach (var pt in down) area.Add(pt);
        area.Add(new Point(SparkWidth, SparkHeight));

        down.Freeze();
        up.Freeze();
        area.Freeze();
        SpeedLine = down;
        SpeedLineUp = up;
        SpeedArea = area;
    }

    private static PointCollection BuildLine(Queue<double> history, double peak)
    {
        var line = new PointCollection();
        double step = SparkWidth / (SparkPoints - 1);
        int index = 0;

        // Older slots with no data yet sit on the baseline, so the line grows in from the left.
        for (int missing = SparkPoints - history.Count; missing > 0; missing--, index++)
            line.Add(new Point(index * step, SparkHeight - 2));

        foreach (double value in history)
        {
            double y = SparkHeight - 2 - (value / peak) * (SparkHeight - 8);
            line.Add(new Point(index * step, y));
            index++;
        }
        return line;
    }

    public string TooltipText => $"Cindrix VPN — {StatusText}";

    // ---------------------------------------------------------------- formatting

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{v:0} {units[u]}" : $"{v:0.00} {units[u]}";
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        return $"{bytesPerSecond / 1024:0.0} KB/s";
    }
}
