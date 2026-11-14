using Microsoft.EntityFrameworkCore;

namespace Remina.Server.Data;

public class ReminaDbContext : DbContext
{
    public ReminaDbContext(DbContextOptions<ReminaDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<AgentCommandRecord> AgentCommands => Set<AgentCommandRecord>();

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
        });

        b.Entity<EnrollmentCode>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
        });
    }
}
