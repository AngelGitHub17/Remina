using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Remina.Agent;
using Remina.Shared;

namespace Remina.Agent.Tray;

/// <summary>
/// Demande de confirmation d'impression avec compte à rebours : sans action au bout de
/// 5 minutes, le ticket est imprimé automatiquement sur l'imprimante par défaut.
/// </summary>
public partial class PrintDialog : Window
{
    private readonly string _text;
    private readonly DispatcherTimer _timer;
    private TimeSpan _remaining = TimeSpan.FromMinutes(5);
    private bool _done;

    public PrintDialog() : this("") { }

    private readonly AgentStatus? _status;

    public PrintDialog(string text)
    {
        InitializeComponent();
        _text = text;
        Preview.Text = text;

        _status = Program.AppHost?.Services.GetService<AgentStatus>();
        if (_status is not null)
        {
            foreach (var p in _status.Printers) PrinterBox.Items.Add(p);
            PrinterBox.SelectedItem = !string.IsNullOrEmpty(_status.PrinterName)
                ? _status.PrinterName
                : (_status.Printers.Count > 0 ? _status.Printers[0] : null);
        }

        UpdateCountdown();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining -= TimeSpan.FromSeconds(1);
            if (_remaining <= TimeSpan.Zero) DoPrint();
            else UpdateCountdown();
        };
        _timer.Start();
    }

    private void UpdateCountdown() =>
        Countdown.Text = $"Impression automatique dans {_remaining:mm\\:ss}";

    private void DoPrint()
    {
        if (_done) return;
        _done = true;
        _timer.Stop();
        var printer = PrinterBox.SelectedItem as string;
        if (_status is not null && !string.IsNullOrEmpty(printer))
        {
            _status.PrinterName = printer;
            _status.OnPrinterChosen?.Invoke(printer); // mémorise le choix
        }
        Printing.Print(_text, printer);
        Close();
    }

    private void OnPrint(object? sender, RoutedEventArgs e) => DoPrint();

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _done = true;
        _timer.Stop();
        Close();
    }
}
