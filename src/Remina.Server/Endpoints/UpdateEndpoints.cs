using Remina.Server.Services;
using Remina.Shared;

namespace Remina.Server.Endpoints;

public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        // L'agent demande s'il doit se mettre à jour (aucune authentification requise :
        // ne révèle qu'une version publique et une URL de téléchargement publique).
        app.MapGet("/api/update", async (
            string app, string rid, string current, UpdateProvider provider, CancellationToken ct) =>
        {
            var result = await provider.CheckAsync(app.ToLowerInvariant(), rid, current, ct);
            return Results.Ok(result);
        });
    }
}
