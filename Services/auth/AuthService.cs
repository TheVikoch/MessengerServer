using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.encryption;
using Microsoft.IdentityModel.Tokens;

namespace MessengerServer.Services.auth;

public class AuthService : IAuthService
{
    private readonly List<User> _users = new();
    private readonly IConfiguration _configuration;
    private readonly IEncryptionService _encryptionService;

    public AuthService(IConfiguration configuration, IEncryptionService encryptionService)
    {
        _configuration = configuration;
        _encryptionService = encryptionService;
    }

    public async Task<JwtResponseDto> RegisterAsync(RegisterDto registerDto)
    {
        // Check if user already exists (compare encrypted emails)
        var encryptedEmail = _encryptionService.Encrypt(registerDto.Email);
        if (_users.Any(u => u.Email == encryptedEmail))
        {
            throw new UserAlreadyExistsException(registerDto.Email);
        }

        // Generate salt and hash password using PBKDF2
        var salt = RandomNumberGenerator.GetBytes(128 / 8); // 128-bit salt
        using var pbkdf2 = new Rfc2898DeriveBytes(registerDto.Password, salt, 10000, HashAlgorithmName.SHA256);
        var passwordHash = Convert.ToBase64String(pbkdf2.GetBytes(256 / 8)); // 256-bit hash

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = encryptedEmail,
            PasswordHash = passwordHash,
            PasswordSalt = Convert.ToBase64String(salt)
        };

        _users.Add(user);

        // Generate JWT token
        var token = GenerateJwtToken(user);

        return new JwtResponseDto
        {
            Token = token,
            Expires = DateTime.UtcNow.AddDays(7),
            Email = registerDto.Email,
            UserId = user.Id
        };
    }

    public async Task<JwtResponseDto> LoginAsync(LoginDto loginDto)
    {
        var encryptedEmail = _encryptionService.Encrypt(loginDto.Email);
        var user = _users.FirstOrDefault(u => u.Email == encryptedEmail);
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

        var token = GenerateJwtToken(user, loginDto.Email);

        return new JwtResponseDto
        {
            Token = token,
            Expires = DateTime.UtcNow.AddDays(7),
            Email = loginDto.Email,
            UserId = user.Id
        };
    }

    public Task<User?> GetUserByIdAsync(Guid id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult(user);
    }

    private string GenerateJwtToken(User user, string? plainEmail = null)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "default_secret_key_change_in_production");

        var email = plainEmail ?? _encryptionService.Decrypt(user.Email);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, email)
        };

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
}
