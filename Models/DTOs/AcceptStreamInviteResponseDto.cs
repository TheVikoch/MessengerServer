using System;

namespace MessengerServer.Models.DTOs
{
    public class AcceptStreamInviteResponseDto
    {
        public Guid InviteId { get; set; }
        public Guid PersonalChatId { get; set; }
        public Guid CreatorId { get; set; }
        public Guid TargetUserId { get; set; }
        public Guid StreamChatId { get; set; }
        public string? StreamChatName { get; set; }
        public DateTime AcceptedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
