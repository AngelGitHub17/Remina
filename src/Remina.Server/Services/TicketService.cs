using System.Text;
using Microsoft.EntityFrameworkCore;
using Remina.Server.Data;

namespace Remina.Server.Services;

/// <summary>Construit le texte du ticket récapitulatif d'un jour (pour impression).</summary>
public static class TicketService
{
    public static async Task<string> BuildTextAsync(ReminaDbContext db, DateOnly day)
    {
        var sb = new StringBuilder();
        sb.AppendLine("====================================");
        sb.AppendLine("     REMINA — Ticket récapitulatif");
        sb.AppendLine($"     {day:dddd d MMMM yyyy}");
        sb.AppendLine("====================================");
        sb.AppendLine();

        // Temps saisi manuellement.
        var entries = (await db.TimeEntries.ToListAsync())
            .Where(e => DateOnly.FromDateTime(e.Start.LocalDateTime) == day)
            .OrderBy(e => e.Start).ToList();

        sb.AppendLine("-- Temps saisi --");
        if (entries.Count == 0) sb.AppendLine("  (aucun)");
        foreach (var e in entries)
            sb.AppendLine($"  {e.Project,-18} {e.Start.LocalDateTime:HH:mm}-{e.End.LocalDateTime:HH:mm}  {Fmt(e.Minutes * 60)}");
        sb.AppendLine($"  Total saisi : {Fmt(entries.Sum(e => e.Minutes) * 60)}");
        sb.AppendLine();

        // Activité automatique par poste.
        var acts = await db.Activity.Where(a => a.Day == day).ToListAsync();
        var ids = acts.Select(a => a.MachineId).Distinct().ToList();
        var names = await db.Machines.Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name);

        sb.AppendLine("-- Activité des postes --");
        if (acts.Count == 0) sb.AppendLine("  (aucune)");
        foreach (var g in acts.GroupBy(a => a.MachineId).OrderByDescending(g => g.Sum(x => x.Seconds)))
        {
            sb.AppendLine($"  {names.GetValueOrDefault(g.Key, "?")} — {Fmt(g.Sum(x => x.Seconds))}");
            foreach (var ag in g.GroupBy(x => x.App).Select(a => new { a.Key, S = a.Sum(x => x.Seconds) })
                         .OrderByDescending(a => a.S).Take(8))
                sb.AppendLine($"     {ag.Key,-18} {Fmt(ag.S)}");
        }
        sb.AppendLine($"  Total activité : {Fmt(acts.Sum(a => a.Seconds))}");
        sb.AppendLine();
        sb.AppendLine($"Généré le {DateTime.Now:dd/MM/yyyy HH:mm}");
        return sb.ToString();
    }

    private static string Fmt(int seconds)
    {
        var h = seconds / 3600; var m = (seconds % 3600) / 60;
        return h > 0 ? $"{h} h {m:D2}" : $"{m} min";
    }
}
