using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class RevokeStreamInviteRequestDto
    {
        [Required]
        public Guid InviteId { get; set; }
    }
}
