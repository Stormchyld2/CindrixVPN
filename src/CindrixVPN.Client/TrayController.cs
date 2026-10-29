using System.IO;
using System.Windows.Resources;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CindrixVPN.Client;

/// <summary>Notification-area icon: left click opens the quick flyout, right click opens the menu.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly VpnSession _session;
    private readonly Func<Task> _exit;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Drawing.Icon _onIcon = LoadIcon("cindrixvpn.ico");
    private readonly Drawing.Icon _offIcon = LoadIcon("cindrixvpn-off.ico");
    private readonly TrayFlyout _flyout;

    public TrayController(VpnSession session, Action openDashboard, Func<Task> exit)
    {
        _session = session;
        _exit = exit;
        _flyout = new TrayFlyout(session, openDashboard);

        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Open dashboard", null, (_, _) => openDashboard());
        _toggleItem = new Forms.ToolStripMenuItem("Connect");
        _toggleItem.Click += async (_, _) => await _session.ToggleAsync();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit CindrixVPN", null, async (_, _) => await _exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _offIcon,
            Text = "CindrixVPN",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _flyout.ToggleVisible();
            }
        };

        _session.Changed += Render;
        Render();
    }

    public void Dispose()
    {
        _session.Changed -= Render;
        _icon.Visible = false;
        _icon.Dispose();
        _onIcon.Dispose();
        _offIcon.Dispose();
        _flyout.Close();
    }

    private static Drawing.Icon LoadIcon(string name)
    {
        StreamResourceInfo info = System.Windows.Application.GetResourceStream(
            new Uri($"pack://application:,,,/Assets/{name}"));
        return new Drawing.Icon(info.Stream);
    }

    private void Render()
    {
        bool connected = _session.State == VpnState.Connected;
        _icon.Icon = connected ? _onIcon : _offIcon;
        string status = _session.State switch
        {
            VpnState.Connected => "Connected",
            VpnState.Connecting => "Connecting…",
            _ => "Disconnected",
        };
        _icon.Text = "CindrixVPN - " + status;
        _toggleItem.Text = _session.IsOn ? "Disconnect" : "Connect";
        _toggleItem.Enabled = _session.Ready && !_session.Busy;
    }
}
