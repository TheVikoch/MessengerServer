using Microsoft.EntityFrameworkCore;
using MessengerServer.Models;

namespace MessengerServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Session> Sessions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.PasswordSalt).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RefreshToken).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.Property(e => e.DeviceInfo).HasMaxLength(256);
            entity.Property(e => e.Ip).HasMaxLength(64);
            entity.Property(e => e.IsRevoked).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        });
    }
}
