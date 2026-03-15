using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class CreateGroupChatDto
    {
        [Required]
        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public List<string> MemberEmails { get; set; } = new List<string>();
    }
}
