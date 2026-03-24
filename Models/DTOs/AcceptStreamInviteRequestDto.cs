using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs
{
    public class AcceptStreamInviteRequestDto
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
