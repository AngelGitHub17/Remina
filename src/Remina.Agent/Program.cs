using Remina.Agent;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Charge appsettings.json à côté de l'exécutable, pas depuis le dossier courant :
    // indispensable quand l'agent tourne en service (cwd imprévisible).
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddHostedService<AgentWorker>();
var host = builder.Build();
host.Run();
