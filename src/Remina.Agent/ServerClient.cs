using System.Net;
using System.Net.Http.Json;
using Remina.Shared;

namespace Remina.Agent;

/// <summary>Issue d'un heartbeat : OK, rejeté (poste inconnu/jeton invalide), ou serveur injoignable.</summary>
public enum HeartbeatResult { Ok, Unauthorized, Unreachable }

/// <summary>Appels HTTP de l'agent vers le serveur Remina.</summary>
public class ServerClient
{
    private readonly HttpClient _http;

    public ServerClient(string baseUrl, TimeSpan? timeout = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = timeout ?? TimeSpan.FromSeconds(15) };
    }

    public async Task<EnrollResponse?> EnrollAsync(EnrollRequest req, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/agent/enroll", req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<EnrollResponse>(ct);
    }

    public async Task<(HeartbeatResult Result, HeartbeatResponse? Response)> HeartbeatAsync(
        string token, HeartbeatRequest req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/agent/heartbeat")
        {
            Content = JsonContent.Create(req),
        };
        msg.Headers.Add("X-Agent-Token", token);
        var resp = await _http.SendAsync(msg, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return (HeartbeatResult.Unauthorized, null);
        if (!resp.IsSuccessStatusCode)
            return (HeartbeatResult.Unreachable, null);
        return (HeartbeatResult.Ok, await resp.Content.ReadFromJsonAsync<HeartbeatResponse>(ct));
    }

    /// <summary>Demande au serveur si une version plus récente est disponible pour cette variante.</summary>
    public async Task<UpdateCheckResult?> CheckUpdateAsync(string app, string rid, string current, CancellationToken ct)
    {
        var url = $"/api/update?app={Uri.EscapeDataString(app)}&rid={Uri.EscapeDataString(rid)}&current={Uri.EscapeDataString(current)}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<UpdateCheckResult>(ct);
    }

    /// <summary>Envoie un rapport d'activité (cumuls depuis le dernier envoi).</summary>
    public async Task<bool> PostActivityAsync(string token, ActivityReport report, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/agent/activity")
        {
            Content = JsonContent.Create(report),
        };
        msg.Headers.Add("X-Agent-Token", token);
        var resp = await _http.SendAsync(msg, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>Télécharge un installeur depuis le serveur vers <paramref name="destPath"/>.</summary>
    public async Task<bool> DownloadPackageAsync(string token, Guid machineId, Guid packageId, string destPath, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get,
            $"/api/agent/package/{packageId}?machineId={machineId}");
        msg.Headers.Add("X-Agent-Token", token);
        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return false;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, ct);
        return true;
    }

    /// <summary>Envoie l'inventaire logiciel (remplace l'existant côté serveur).</summary>
    public async Task<bool> PostInventoryAsync(string token, InventoryReport report, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/agent/inventory")
        { Content = JsonContent.Create(report) };
        msg.Headers.Add("X-Agent-Token", token);
        var resp = await _http.SendAsync(msg, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>Remonte le résultat d'une commande (ex : installation).</summary>
    public async Task PostCommandResultAsync(string token, CommandResult result, CancellationToken ct)
    {
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/agent/command-result")
            { Content = JsonContent.Create(result) };
            msg.Headers.Add("X-Agent-Token", token);
            await _http.SendAsync(msg, ct);
        }
        catch { }
    }

    /// <summary>Envoie une entrée de temps (étape de récap lancée depuis l'interface).</summary>
    public async Task<bool> PostTimeEntryAsync(string token, AgentTimeEntryReport report, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/agent/time-entry")
        { Content = JsonContent.Create(report) };
        msg.Headers.Add("X-Agent-Token", token);
        var resp = await _http.SendAsync(msg, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>HttpClient dédié aux téléchargements (timeout long).</summary>
    public static HttpClient DownloadClient() => new() { Timeout = TimeSpan.FromMinutes(5) };
}
