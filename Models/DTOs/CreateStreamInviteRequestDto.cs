using System;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class CreateStreamInviteRequestDto
    {
        [Required]
        public Guid PersonalChatId { get; set; }

        public string? StreamChatName { get; set; }
    }
}
