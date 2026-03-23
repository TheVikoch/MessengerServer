using System;

namespace MessengerServer.Models.DTOs
{
    public class MessageAttachmentDto
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public MediaEncryptionMetadataDto? Encryption { get; set; }
    }
}
