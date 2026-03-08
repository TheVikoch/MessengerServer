using MessengerServer.Models;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.auth;

public interface IAuthService
{
    Task<JwtResponseDto> RegisterAsync(RegisterDto registerDto);
    Task<JwtResponseDto> LoginAsync(LoginDto loginDto);
    Task<User?> GetUserByIdAsync(Guid id);
    Task<IEnumerable<Session>> GetSessionsForUserAsync(Guid userId);
    Task RevokeSessionAsync(Guid sessionId, Guid userId);
}
