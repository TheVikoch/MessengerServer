using System;

namespace MessengerServer.Models.DTOs
{
    public class InitUploadResponseDto
    {
        public string AttachmentId { get; set; } = string.Empty;
        public string UploadUrl { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
