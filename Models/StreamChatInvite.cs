using System;

namespace MessengerServer.Models
{
    public class StreamChatInvite
    {
        public Guid Id { get; set; }
        public Guid CreatorId { get; set; }
        public Guid TargetUserId { get; set; }
        public Guid PersonalChatId { get; set; }
        public Guid? StreamChatId { get; set; }
        public string? StreamChatName { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending/Accepted/Revoked/Expired
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
