using System;

namespace MessengerServer.Models.DTOs
{
    public class StreamInviteMetadataDto
    {
        public Guid InviteId { get; set; }
        public Guid PersonalChatId { get; set; }
        public Guid CreatorId { get; set; }
        public Guid TargetUserId { get; set; }
        public Guid? StreamChatId { get; set; }
        public string Status { get; set; } = string.Empty; // accepted/revoked/expired
        public DateTime ExpiresAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
