using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Remina.Agent.Tray;

public partial class App : Application
{
    private MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _window = new MainWindow();

            // Icône de la barre des tâches (zone de notification) + menu.
            var open = new NativeMenuItem("Ouvrir");
            open.Click += (_, _) => ShowWindow();
            var quit = new NativeMenuItem("Quitter");
            quit.Click += (_, _) => desktop.Shutdown();

            var tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Remina.Agent.Tray/Assets/remina.png"))),
                ToolTipText = "Remina Agent",
                Menu = new NativeMenu { open, quit },
            };
            tray.Clicked += (_, _) => ShowWindow();
            TrayIcon.SetIcons(this, new TrayIcons { tray });

            // Démarre réduit dans la barre des tâches ; la fenêtre s'ouvre à la demande.
            desktop.MainWindow = _window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
