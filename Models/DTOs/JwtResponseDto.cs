namespace MessengerServer.Models.DTOs;

public class JwtResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    // Refresh token issued for the session
    public string RefreshToken { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
}
