using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Infrastructure;

public sealed class SecurityDbContext(DbContextOptions<SecurityDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<AuthSession> Sessions => Set<AuthSession>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<RecoveryAttempt> RecoveryAttempts => Set<RecoveryAttempt>();
    public DbSet<SecurityEvent> Events => Set<SecurityEvent>();
    public DbSet<OutgoingEmail> Emails => Set<OutgoingEmail>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppRole>(e =>
        {
            e.ToTable("roles"); e.HasKey(x => x.Name); e.Property(x => x.Name).HasMaxLength(30);
            e.HasData(Ameli.Contracts.Roles.All.Select(n => new AppRole { Name = n }));
        });
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.NormalizedEmail).HasMaxLength(254);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.RoleName).HasMaxLength(30);
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.HasOne<AppRole>().WithMany().HasForeignKey(x => x.RoleName).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AuthSession>(e =>
        {
            e.ToTable("auth_sessions"); e.HasKey(x => x.Id);
            e.Property(x => x.RefreshTokenHash).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Device).HasMaxLength(240);
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PasswordReset>(e =>
        {
            e.ToTable("password_resets"); e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RecoveryAttempt>(e =>
        {
            e.ToTable("recovery_attempts"); e.HasKey(x => x.Id);
            e.Property(x => x.AccountKey).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Origin).HasMaxLength(80);
            e.HasIndex(x => new { x.AccountKey, x.CreatedAtUtc });
            e.HasIndex(x => new { x.Origin, x.CreatedAtUtc });
        });
        b.Entity<SecurityEvent>(e =>
        {
            e.ToTable("security_events"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.Outcome).HasMaxLength(30);
            e.Property(x => x.ActorRole).HasMaxLength(30);
            e.Property(x => x.Origin).HasMaxLength(80);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.Property(x => x.Detail).HasMaxLength(1500);
            e.HasIndex(x => x.OccurredAtUtc);
        });
        b.Entity<OutgoingEmail>(e =>
        {
            e.ToTable("email_outbox"); e.HasKey(x => x.Id);
            e.Property(x => x.Recipient).HasMaxLength(254);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.HasIndex(x => new { x.SentAtUtc, x.NextAttemptAtUtc });
        });
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name,
                    "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
