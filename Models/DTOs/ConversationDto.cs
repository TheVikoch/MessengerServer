using System;
using System.Collections.Generic;

namespace MessengerServer.Models.DTOs
{
    public class ConversationDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty; // "personal" or "group"
        public string? Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string? LastMessageContent { get; set; }
        public bool IsDeleted { get; set; }
        public List<ConversationMemberDto> Members { get; set; } = new List<ConversationMemberDto>();
    }
}
