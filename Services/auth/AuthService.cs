using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;

namespace MessengerServer.Services.auth;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly MessengerServer.Services.encryption.IEncryptionService _encryptionService;

    public AuthService(AppDbContext context, IConfiguration configuration, MessengerServer.Services.encryption.IEncryptionService encryptionService)
    {
        _context = context;
        _configuration = configuration;
        _encryptionService = encryptionService;
    }

    public async Task<JwtResponseDto> RegisterAsync(RegisterDto registerDto)
    {
        var encryptedEmail = _encryptionService.Encrypt(registerDto.Email);

        if (await _context.Users.AnyAsync(u => u.Email == encryptedEmail))
        {
            throw new UserAlreadyExistsException(registerDto.Email);
        }

        var salt = RandomNumberGenerator.GetBytes(128 / 8);
        using var pbkdf2 = new Rfc2898DeriveBytes(registerDto.Password, salt, 10000, HashAlgorithmName.SHA256);
        var passwordHash = Convert.ToBase64String(pbkdf2.GetBytes(256 / 8));

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = encryptedEmail,
            PasswordHash = passwordHash,
            PasswordSalt = Convert.ToBase64String(salt),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // create refresh token and session for the newly registered user
        var refreshToken = GenerateRefreshToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            DeviceInfo = string.Empty,
            Ip = string.Empty,
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        // Use decrypted email for token/response
        var token = GenerateJwtToken(new User { Id = user.Id, Email = registerDto.Email }, session.Id);

        return new JwtResponseDto
        {
            Token = token,
            Expires = DateTime.UtcNow.AddDays(7),
            Email = registerDto.Email,
            UserId = user.Id,
            RefreshToken = refreshToken,
            SessionId = session.Id
        };
    }

    public async Task<JwtResponseDto> LoginAsync(LoginDto loginDto)
    {
        var encryptedEmail = _encryptionService.Encrypt(loginDto.Email);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == encryptedEmail);
        if (user == null)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var saltBytes = Convert.FromBase64String(user.PasswordSalt);
        using var pbkdf2 = new Rfc2898DeriveBytes(loginDto.Password, saltBytes, 10000, HashAlgorithmName.SHA256);
        var computedHash = Convert.ToBase64String(pbkdf2.GetBytes(256 / 8));

        if (computedHash != user.PasswordHash)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var device = loginDto.DeviceInfo ?? string.Empty;
        var ip = loginDto.Ip ?? string.Empty;

        // First try to find an active (non-revoked) session from the same device
        var existingSession = await _context.Sessions.FirstOrDefaultAsync(s =>
            s.UserId == user.Id && s.DeviceInfo == device && s.Ip == ip && !s.IsRevoked && s.ExpiresAt > DateTime.UtcNow);

        if (existingSession != null)
        {
            existingSession.ExpiresAt = DateTime.UtcNow.AddDays(30);
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(new User { Id = user.Id, Email = loginDto.Email }, existingSession.Id);

            return new JwtResponseDto
            {
                Token = token,
                Expires = DateTime.UtcNow.AddDays(7),
                Email = loginDto.Email,
                UserId = user.Id,
                RefreshToken = existingSession.RefreshToken,
                SessionId = existingSession.Id
            };
        }

        // Check if there's a revoked session from the same device - restore it
        var revokedSession = await _context.Sessions.FirstOrDefaultAsync(s =>
            s.UserId == user.Id && s.DeviceInfo == device && s.Ip == ip && s.IsRevoked);

        if (revokedSession != null)
        {
            // Restore the revoked session
            revokedSession.IsRevoked = false;
            revokedSession.ExpiresAt = DateTime.UtcNow.AddDays(30);
            // Generate new refresh token for security
            revokedSession.RefreshToken = GenerateRefreshToken();
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(new User { Id = user.Id, Email = loginDto.Email }, revokedSession.Id);

            return new JwtResponseDto
            {
                Token = token,
                Expires = DateTime.UtcNow.AddDays(7),
                Email = loginDto.Email,
                UserId = user.Id,
                RefreshToken = revokedSession.RefreshToken,
                SessionId = revokedSession.Id
            };
        }

        // No session exists for this device - create a new one
        var refreshToken = GenerateRefreshToken();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            DeviceInfo = device,
            Ip = ip,
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        var tokenNew = GenerateJwtToken(new User { Id = user.Id, Email = loginDto.Email }, session.Id);

        return new JwtResponseDto
        {
            Token = tokenNew,
            Expires = DateTime.UtcNow.AddDays(7),
            Email = loginDto.Email,
            UserId = user.Id,
            RefreshToken = refreshToken,
            SessionId = session.Id
        };
    }

    public async Task<IEnumerable<Session>> GetSessionsForUserAsync(Guid userId)
    {
        return await _context.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task RevokeSessionAsync(Guid sessionId, Guid userId)
    {
        var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session == null) throw new KeyNotFoundException("Session not found");

        session.IsRevoked = true;
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ValidateSessionAsync(Guid sessionId, Guid userId)
    {
        var session = await _context.Sessions.FirstOrDefaultAsync(s =>
            s.Id == sessionId &&
            s.UserId == userId &&
            !s.IsRevoked &&
            s.ExpiresAt > DateTime.UtcNow);
        
        return session != null;
    }

    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        try
        {
            user.Email = _encryptionService.Decrypt(user.Email);
        }
        catch
        {
            throw new Exception("Failed to decrypt user email.");
        }

        return user;
    }

    private string GenerateJwtToken(User user, Guid? sessionId = null)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "default_secret_key_change_in_production");

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email)
        };

        if (sessionId.HasValue)
        {
            claims.Add(new Claim("sessionId", sessionId.Value.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256
        );

        var expires = DateTime.UtcNow.AddDays(7);

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"] ?? "MessengerServer",
            audience: jwtSettings["Audience"] ?? "MessengerClient",
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
