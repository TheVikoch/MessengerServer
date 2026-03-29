using System.Collections.Generic;

namespace MessengerServer.Models.DTOs
{
    public class ConversationAttachmentsResponseDto
    {
        public List<ConversationAttachmentEntryDto> Attachments { get; set; } = new();
        public bool HasMore { get; set; } = false;
        public string? NextCursor { get; set; }
    }
}
