using Remina.Agent.Platform;
using Remina.Shared;

namespace Remina.Agent;

/// <summary>
/// Accumule localement le temps de premier plan par (jour, appli, titre).
/// Ne mesure que ce qu'on lui demande d'échantillonner : l'appelant saute les périodes
/// d'inactivité et le mode privé. Les titres ne sont capturés que si activé.
/// </summary>
public class ActivitySampler
{
    private readonly IPlatform _platform;
    private readonly bool _captureTitles;
    private readonly Dictionary<(DateOnly Day, string App, string? Title), int> _acc = new();
    private readonly object _lock = new();

    public ActivitySampler(IPlatform platform, bool captureTitles)
    {
        _platform = platform;
        _captureTitles = captureTitles;
    }

    /// <summary>Attribue <paramref name="seconds"/> à l'appli actuellement au premier plan.</summary>
    public void Sample(int seconds)
    {
        var fg = _platform.GetForegroundApp();
        if (fg is null) return;
        var key = (DateOnly.FromDateTime(DateTime.Now), fg.Value.App,
                   _captureTitles ? fg.Value.Title : null);
        lock (_lock) _acc[key] = _acc.GetValueOrDefault(key) + seconds;
    }

    public bool HasData { get { lock (_lock) return _acc.Count > 0; } }

    /// <summary>Récupère et vide les tranches accumulées (à envoyer au serveur).</summary>
    public IReadOnlyList<ActivityEntry> Drain()
    {
        lock (_lock)
        {
            var list = _acc.Select(kv => new ActivityEntry
            {
                Day = kv.Key.Day, App = kv.Key.App, Title = kv.Key.Title, Seconds = kv.Value,
            }).ToList();
            _acc.Clear();
            return list;
        }
    }
}
