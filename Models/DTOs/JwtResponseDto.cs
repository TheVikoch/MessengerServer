namespace MessengerServer.Models.DTOs;

public class JwtResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}
