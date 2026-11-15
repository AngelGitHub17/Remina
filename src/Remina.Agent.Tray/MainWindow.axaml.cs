using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Remina.Agent;

namespace Remina.Agent.Tray;

public partial class MainWindow : Window
{
    private readonly AgentStatus? _status;
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();
        _status = Program.AppHost?.Services.GetService<AgentStatus>();

        // Fermer = réduire dans la barre des tâches, pas quitter.
        Closing += (s, e) => { e.Cancel = true; Hide(); };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        if (_status is null) return;
        VersionText.Text = _status.Version;
        ServerText.Text = string.IsNullOrEmpty(_status.ServerUrl) ? "—" : _status.ServerUrl;
        MachineText.Text = _status.Enrolled ? _status.MachineName : "non inscrit";
        RdpText.Text = _status.RemoteSessionActive ? "oui (auto-login en pause)" : "non";

        if (_status.LastHeartbeatOk is { } hb)
        {
            var s = (int)(DateTimeOffset.UtcNow - hb).TotalSeconds;
            HeartbeatText.Text = s < 5 ? "à l'instant" : $"il y a {s} s";
        }
        else HeartbeatText.Text = "jamais";

        var online = _status.Online;
        StateText.Text = online ? "en ligne" : (_status.Enrolled ? "hors ligne" : "en attente d'inscription");
        StatusDot.Fill = new SolidColorBrush(online ? Color.Parse("#3ecf8e") : Color.Parse("#6b7280"));
        ErrorText.Text = _status.LastError ?? "";

        if (!_status.ActivityEnabled) ActivityText.Text = "désactivé";
        else if (_status.PrivateMode) ActivityText.Text = "en pause (mode privé)";
        else ActivityText.Text = "actif";
        PrivateToggle.IsChecked = _status.PrivateMode;
        PrivateToggle.IsEnabled = _status.ActivityEnabled;
    }

    private void OnTogglePrivate(object? sender, RoutedEventArgs e)
    {
        if (_status is null) return;
        _status.PrivateMode = PrivateToggle.IsChecked ?? false;
        _status.NotifyChanged();
    }

    private void OnHide(object? sender, RoutedEventArgs e) => Hide();
}
