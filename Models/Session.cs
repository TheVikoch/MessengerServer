using System;

namespace MessengerServer.Models;

public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public bool IsRevoked { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
