using System;

namespace MessengerServer.Models
{
    public class MessageAttachment
    {
        public string Id { get; set; } = string.Empty;
        public string ObjectKey { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public MediaEncryptionMetadata? Encryption { get; set; }
    }
}
