namespace MessengerServer.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public bool TwoFactorEnabled { get; set; } = false;
    public string? TwoFactorSecret { get; set; }

    // Navigation property for sessions
    public IList<Session> Sessions { get; set; } = new List<Session>();
}
