using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class RemoveMemberDto
    {
        [Required]
        public Guid UserId { get; set; }
    }
}
