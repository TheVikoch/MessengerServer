using System;

namespace MessengerServer.Models.DTOs
{
    public class CreateStreamInviteResponseDto
    {
        public Guid InviteId { get; set; }
        public Guid PersonalChatId { get; set; }
        public Guid CreatorId { get; set; }
        public Guid TargetUserId { get; set; }
        public string Token { get; set; } = string.Empty;
        public string? StreamChatName { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
