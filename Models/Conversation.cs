using System;
using System.Collections.Generic;

namespace MessengerServer.Models
{
    public class Conversation
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty; // "personal" or "group"
        public string? Name { get; set; } // Null for personal chats, required for group chats
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastMessageAt { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Navigation properties
        public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
    }
}
