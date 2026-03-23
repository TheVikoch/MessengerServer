using System;
using System.Collections.Generic;

namespace MessengerServer.Models
{
    public class Message
    {
        public string Id { get; set; } = string.Empty; // MongoDB ObjectId as string
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public string EncryptedContent { get; set; } = string.Empty; // Encrypted message content
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        
        // Optional: for reply chain
        public string? ReplyToMessageId { get; set; }

        public List<MessageAttachment> Attachments { get; set; } = new();
    }
}
