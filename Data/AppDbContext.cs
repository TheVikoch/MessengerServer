using Microsoft.EntityFrameworkCore;
using MessengerServer.Models;

namespace MessengerServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<UserProfilePhoto> UserProfilePhotos { get; set; } = null!;
    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<Conversation> Conversations { get; set; } = null!;
    public DbSet<ConversationMember> ConversationMembers { get; set; } = null!;
    public DbSet<StreamChatInvite> StreamChatInvites { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(64);
            entity.Property(e => e.AboutMe).HasMaxLength(1024);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.PasswordSalt).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.DisplayName).IsUnique();
        });

        modelBuilder.Entity<UserProfilePhoto>(entity =>
        {
            entity.ToTable("UserProfilePhotos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ObjectKey).IsRequired().HasMaxLength(512);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => new { e.UserId, e.IsDeleted, e.CreatedAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.ProfilePhotos)
                .HasForeignKey(e => e.UserId)
                .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.IsDeleted).IsRequired();
        });

        modelBuilder.Entity<ConversationMember>(entity =>
        {
            entity.ToTable("ConversationMembers");
            entity.HasKey(e => new { e.ConversationId, e.UserId });
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.JoinedAt).IsRequired();
            entity.Property(e => e.IsPinned).IsRequired();

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Members)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
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

        modelBuilder.Entity<StreamChatInvite>(entity =>
        {
            entity.ToTable("StreamChatInvites");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatorId).IsRequired();
            entity.Property(e => e.TargetUserId).IsRequired();
            entity.Property(e => e.PersonalChatId).IsRequired();
            entity.Property(e => e.Token).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.StreamChatName).HasMaxLength(256);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();

            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => new { e.PersonalChatId, e.Status });
        });
    }
}

