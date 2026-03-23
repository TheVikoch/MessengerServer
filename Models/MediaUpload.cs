using System;

namespace MessengerServer.Models
{
    public class MediaUpload
    {
        public string Id { get; set; } = string.Empty;
        public Guid ConversationId { get; set; }
        public Guid UserId { get; set; }
        public string ObjectKey { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public MediaEncryptionMetadata? Encryption { get; set; }
    }
}
