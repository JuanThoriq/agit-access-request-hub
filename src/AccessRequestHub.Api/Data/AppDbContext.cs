using AccessRequestHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Application>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.SystemOwner).WithMany().HasForeignKey(x => x.SystemOwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AccessRequest>(e =>
        {
            e.Property(x => x.ClientRequestId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Justification).HasMaxLength(2000).IsRequired();
            e.Property(x => x.PolicyVersion).HasMaxLength(20).IsRequired();

            // Stored as text: a broken value fails loudly on write, and the DB stays readable by hand.
            e.Property(x => x.Environment).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AccessLevel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);

            // Idempotency is a database guarantee, not an application check: a concurrent retry
            // cannot create a second business row even if two requests race past the same read.
            e.HasIndex(x => new { x.RequesterId, x.ClientRequestId }).IsUnique();

            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.RequesterId, x.CreatedAtUtc });

            e.Property(x => x.Version).IsConcurrencyToken();

            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_AccessRequests_Environment", "Environment IN ('NonProduction','Production')");
                t.HasCheckConstraint("CK_AccessRequests_AccessLevel", "AccessLevel IN ('Read','Admin')");
                t.HasCheckConstraint(
                    "CK_AccessRequests_Status",
                    "Status IN ('PendingManagerApproval','PendingSystemOwnerApproval','Approved','Rejected')");
                t.HasCheckConstraint("CK_AccessRequests_Version", "Version >= 1");
                t.HasCheckConstraint("CK_AccessRequests_Justification", "length(trim(Justification)) > 0");
            });
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.EventType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40);

            e.HasIndex(x => new { x.AccessRequestId, x.Id });
            e.HasOne(x => x.AccessRequest).WithMany(r => r.AuditEvents).HasForeignKey(x => x.AccessRequestId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);

            // Append-only is enforced by the database (see the trigger in the migration), so a bug in
            // application code cannot rewrite history: UPDATE/DELETE on this table abort.
            e.ToTable(t => t.HasCheckConstraint("CK_AuditEvents_Transition", "FromStatus IS NULL OR FromStatus <> ToStatus"));
        });
    }
}
