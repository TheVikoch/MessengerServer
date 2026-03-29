using System;

namespace MessengerServer.Models.DTOs
{
    public class InitConversationAvatarUploadResponseDto
    {
        public Guid PhotoId { get; set; }
        public string UploadUrl { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
