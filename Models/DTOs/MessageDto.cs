using System;

namespace MessengerServer.Models.DTOs
{
    public class MessageDto
    {
        public string Id { get; set; } = string.Empty;
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public UserDto? Sender { get; set; }
        public string Content { get; set; } = string.Empty; // Decrypted content
        public DateTime SentAt { get; set; }
        public bool IsDeleted { get; set; }
        public string? ReplyToMessageId { get; set; }
        public List<MessageAttachmentDto> Attachments { get; set; } = new();
    }
}
