using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models.DTOs;

public class LoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // Optional fields to help identify device / ip for session tracking
    public string? DeviceInfo { get; set; }
    public string? Ip { get; set; }
}
