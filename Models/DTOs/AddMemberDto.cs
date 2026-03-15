using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class AddMemberDto
    {
        [Required]
        [EmailAddress]
        public string UserEmail { get; set; } = string.Empty;
    }
}
