using System;

namespace MessengerServer.Models
{
    public class DeletedMessageForUser
    {
        public Guid UserId { get; set; }
        public Guid ConversationId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

        public User? User { get; set; }
    }
}
