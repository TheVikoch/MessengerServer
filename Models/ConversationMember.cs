using System;
using System.Collections.Generic;

namespace MessengerServer.Models
{
    public class ConversationMember
    {
        public Guid ConversationId { get; set; }
        public Guid UserId { get; set; }
        public string Role { get; set; } = "member"; // "creator", "admin", "member"
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsPinned { get; set; } = false;
        public string? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }

        // Navigation properties
        public Conversation? Conversation { get; set; }
        public User? User { get; set; }
    }
}
