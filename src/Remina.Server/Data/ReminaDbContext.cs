using Microsoft.EntityFrameworkCore;

namespace Remina.Server.Data;

public class ReminaDbContext : DbContext
{
    public ReminaDbContext(DbContextOptions<ReminaDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Bureau> Bureaus => Set<Bureau>();
    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<AgentCommandRecord> AgentCommands => Set<AgentCommandRecord>();
    public DbSet<ActivityAggregate> Activity => Set<ActivityAggregate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Machine>(e =>
        {
            e.HasIndex(m => m.HardwareId).IsUnique();
            e.HasIndex(m => m.AgentTokenHash);
            e.HasMany(m => m.Commands)
             .WithOne(c => c.Machine!)
             .HasForeignKey(c => c.MachineId)
             .OnDelete(DeleteBehavior.Cascade);
            // Supprimer un bureau détache ses postes (ne les supprime pas).
            e.HasOne(m => m.Bureau)
             .WithMany(bu => bu.Machines)
             .HasForeignKey(m => m.BureauId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Bureau>(e =>
        {
            e.HasIndex(bu => bu.Name).IsUnique();
        });

        b.Entity<EnrollmentCode>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
        });

        b.Entity<ActivityAggregate>(e =>
        {
            // Une ligne par (poste, jour, appli, titre) : on y cumule les secondes.
            e.HasIndex(a => new { a.MachineId, a.Day, a.App, a.Title }).IsUnique();
            e.HasOne(a => a.Machine).WithMany()
             .HasForeignKey(a => a.MachineId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
