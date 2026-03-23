using System;

namespace MessengerServer.Models.DTOs
{
    public class CompleteUploadRequestDto
    {
        public Guid ConversationId { get; set; }
        public string AttachmentId { get; set; } = string.Empty;
    }
}
