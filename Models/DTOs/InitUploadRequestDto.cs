using System;

namespace MessengerServer.Models.DTOs
{
    public class InitUploadRequestDto
    {
        public Guid ConversationId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
