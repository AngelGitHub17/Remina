using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Remina.Agent.Tray;

public partial class AutoLoginDialog : Window
{
    public AutoLoginDialog() => InitializeComponent();

    private void OnEnable(object? sender, RoutedEventArgs e)
    {
        var user = UserBox.Text?.Trim() ?? "";
        var pass = PassBox.Text ?? "";
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) return;
        AutoLoginSetup.RequestApply(user, pass, string.IsNullOrWhiteSpace(DomainBox.Text) ? null : DomainBox.Text!.Trim());
        Close();
    }

    private void OnDisable(object? sender, RoutedEventArgs e)
    {
        AutoLoginSetup.RequestDisable();
        Close();
    }
}
