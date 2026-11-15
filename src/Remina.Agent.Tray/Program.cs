using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Remina.Agent;

namespace Remina.Agent.Tray;

internal static class Program
{
    /// <summary>Hôte de fond qui fait tourner le cœur de l'agent (inscription + heartbeat).</summary>
    public static IHost? AppHost { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        AppHost = BuildHost(args);
        AppHost.Start();

        // OnExplicitShutdown : fermer la fenêtre ne quitte pas l'appli (reste en barre des tâches).
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

        AppHost.StopAsync().GetAwaiter().GetResult();
        AppHost.Dispose();
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
        builder.Services.AddSingleton<AgentStatus>();
        builder.Services.AddHostedService<AgentWorker>();
        return builder.Build();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
