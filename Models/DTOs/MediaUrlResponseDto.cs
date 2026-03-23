using System;

namespace MessengerServer.Models.DTOs
{
    public class MediaUrlResponseDto
    {
        public string Url { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
