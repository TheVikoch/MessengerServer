using System;

namespace MessengerServer.Models.DTOs
{
    public class UserSearchResultDto
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public Guid? ExistingConversationId { get; set; }
        public Guid? LatestProfilePhotoId { get; set; }
    }
}
