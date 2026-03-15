using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class CreatePersonalChatDto
    {
        [Required]
        [EmailAddress]
        public string UserEmail { get; set; } = string.Empty;
    }
}
