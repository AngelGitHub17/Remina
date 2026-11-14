using System.Net.Http.Json;
using Remina.Shared;

namespace Remina.Agent;

/// <summary>Appels HTTP de l'agent vers le serveur Remina.</summary>
public class ServerClient
{
    private readonly HttpClient _http;

    public ServerClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<EnrollResponse?> EnrollAsync(EnrollRequest req, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/agent/enroll", req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<EnrollResponse>(ct);
    }

    public async Task<HeartbeatResponse?> HeartbeatAsync(string token, HeartbeatRequest req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/agent/heartbeat")
        {
            Content = JsonContent.Create(req),
        };
        msg.Headers.Add("X-Agent-Token", token);
        var resp = await _http.SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<HeartbeatResponse>(ct);
    }
}
