using System;

namespace MessengerServer.Models.DTOs
{
    public class ConversationAttachmentEntryDto
    {
        public Guid ConversationId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string SenderLabel { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public MessageAttachmentDto Attachment { get; set; } = new();
    }
}
