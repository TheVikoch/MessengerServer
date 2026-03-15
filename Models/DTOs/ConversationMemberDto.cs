using System;

namespace MessengerServer.Models.DTOs
{
    public class ConversationMemberDto
    {
        public Guid UserId { get; set; }
        public UserDto User { get; set; } = new UserDto();
        public string Role { get; set; } = "member";
        public DateTime JoinedAt { get; set; }
        public bool IsPinned { get; set; }
    }
}
