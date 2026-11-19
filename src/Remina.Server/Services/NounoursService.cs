using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Remina.Server.Data;

namespace Remina.Server.Services;

/// <summary>
/// Client du pont SSO NounoursAccount (<c>/api/remina-sso.php</c>, PHP), appelé de serveur à
/// serveur avec le secret partagé. Configuration <c>Remina:Nounours:*</c> :
/// <list type="bullet">
/// <item><c>Enabled</c> (false), <c>BaseUrl</c> (https://didikaria.fr), <c>ClientSecret</c> (≥ 32 car.),</item>
/// <item><c>PublicUrl</c> : adresse publique de Remina (ex. https://remina.didikaria.fr),</item>
/// <item><c>MinRoleCode</c> (2 = admin NounoursAccount), <c>DefaultRole</c> (ReadOnly),</item>
/// <item><c>CheckMinutes</c> (2) : fréquence de vérification du lien de session,</item>
/// <item><c>GraceMinutes</c> (30) : tolérance si NounoursAccount est injoignable.</item>
/// </list>
/// </summary>
public sealed class NounoursService
{
    public const string Provider = "nounours";
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<NounoursService> _log;

    public NounoursService(IHttpClientFactory http, IConfiguration cfg, ILogger<NounoursService> log)
    {
        _http = http;
        _cfg = cfg;
        _log = log;
    }

    private string BaseUrl => (_cfg["Remina:Nounours:BaseUrl"] ?? "https://didikaria.fr").TrimEnd('/');
    private string Secret => _cfg["Remina:Nounours:ClientSecret"] ?? "";
    public string PublicUrl => (_cfg["Remina:Nounours:PublicUrl"] ?? "").TrimEnd('/');
    public int MinRoleCode => _cfg.GetValue("Remina:Nounours:MinRoleCode", 2);
    public TimeSpan CheckEvery => TimeSpan.FromMinutes(_cfg.GetValue("Remina:Nounours:CheckMinutes", 2.0));
    public TimeSpan Grace => TimeSpan.FromMinutes(_cfg.GetValue("Remina:Nounours:GraceMinutes", 30.0));

    public AdminRole DefaultRole =>
        Enum.TryParse<AdminRole>(_cfg["Remina:Nounours:DefaultRole"], true, out var r) ? r : AdminRole.ReadOnly;

    public bool Enabled =>
        _cfg.GetValue("Remina:Nounours:Enabled", false)
        && Secret.Length >= 32
        && Uri.TryCreate(PublicUrl, UriKind.Absolute, out _);

    /// <summary>Page de connexion NounoursAccount, qui reviendra sur notre callback avec ?sso_token=.</summary>
    public string LoginUrl(string state)
    {
        var callback = $"{PublicUrl}/api/auth/nounours/callback?state={Uri.EscapeDataString(state)}";
        return $"{BaseUrl}/connexion.php?redirect={Uri.EscapeDataString(callback)}";
    }

    public sealed record NaUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("role_code")] int RoleCode,
        [property: JsonPropertyName("link")] string Link);

    public sealed record NaCheck(
        [property: JsonPropertyName("active")] bool Active,
        [property: JsonPropertyName("role_code")] int? RoleCode);

    /// <summary>Échange un sso_token (usage unique) contre l'identité. Null si refusé.</summary>
    public async Task<NaUser?> ExchangeAsync(string ssoToken, CancellationToken ct = default)
    {
        using var resp = await PostAsync(new { action = "exchange", sso_token = ssoToken }, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("NounoursAccount : échange refusé ({Status}).", (int)resp.StatusCode);
            return null;
        }
        var u = await resp.Content.ReadFromJsonAsync<NaUser>(ct);
        return u is { Id: > 0 } && u.Link?.Length == 64 ? u : null;
    }

    /// <summary>
    /// État du lien de session : true/false, ou null si NounoursAccount ne répond pas
    /// (l'appelant applique alors la tolérance <see cref="Grace"/>).
    /// </summary>
    public async Task<NaCheck?> CheckAsync(string link, CancellationToken ct = default)
    {
        try
        {
            using var resp = await PostAsync(new { action = "check", link }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<NaCheck>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log.LogWarning("NounoursAccount injoignable pour la vérification : {Msg}", ex.Message);
            return null;
        }
    }

    public async Task RevokeAsync(string link)
    {
        try { using var _ = await PostAsync(new { action = "revoke", link }, CancellationToken.None); }
        catch (Exception ex) { _log.LogWarning("NounoursAccount : révocation du lien impossible : {Msg}", ex.Message); }
    }

    private Task<HttpResponseMessage> PostAsync(object body, CancellationToken ct)
    {
        var client = _http.CreateClient(nameof(NounoursService));
        client.Timeout = TimeSpan.FromSeconds(10);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/remina-sso.php")
        {
            // Corps de taille fixe (Content-Length) : certains PHP-FPM gèrent mal le « chunked ».
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Remina-Secret", Secret);
        return client.SendAsync(req, ct);
    }
}
