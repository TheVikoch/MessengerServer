using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class SendMessageDto
    {
        [Required]
        public Guid ConversationId { get; set; }
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        public string? ReplyToMessageId { get; set; }
    }
}
