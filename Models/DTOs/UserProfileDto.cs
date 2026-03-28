using System;
using System.Collections.Generic;

namespace MessengerServer.Models.DTOs
{
    public class UserProfileDto
    {
        public Guid UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? AboutMe { get; set; }
        public Guid? LatestProfilePhotoId { get; set; }
        public List<UserProfilePhotoDto> Photos { get; set; } = new();
    }
}
