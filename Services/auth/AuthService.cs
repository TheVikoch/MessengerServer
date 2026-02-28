using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using Microsoft.IdentityModel.Tokens;

namespace MessengerServer.Services.auth;

public class AuthService : IAuthService
{
    private readonly List<User> _users = new();
    private readonly IConfiguration _configuration;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<JwtResponseDto> RegisterAsync(RegisterDto registerDto)
    {
        // Check if user already exists
        if (_users.Any(u => u.Email == registerDto.Email))
        {
            throw new InvalidOperationException($"������������ � ������ {registerDto.Email} ��� ����������");
        }

        // Generate salt and hash password using PBKDF2
        var salt = RandomNumberGenerator.GetBytes(128 / 8); // 128-bit salt
        using var pbkdf2 = new Rfc2898DeriveBytes(registerDto.Password, salt, 10000, HashAlgorithmName.SHA256);
        var passwordHash = Convert.ToBase64String(pbkdf2.GetBytes(256 / 8)); // 256-bit hash

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = registerDto.Email,
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
            Email = user.Email,
            UserId = user.Id
        };
    }

    public async Task<JwtResponseDto> LoginAsync(LoginDto loginDto)
    {
        var user = _users.FirstOrDefault(u => u.Email == loginDto.Email);
        if (user == null)
        {
            throw new UnauthorizedAccessException("�������� email ��� ������ ");
        }

        // Verify password using PBKDF2
        var saltBytes = Convert.FromBase64String(user.PasswordSalt);
        using var pbkdf2 = new Rfc2898DeriveBytes(loginDto.Password, saltBytes, 10000, HashAlgorithmName.SHA256);
        var computedHash = Convert.ToBase64String(pbkdf2.GetBytes(256 / 8));

        if (computedHash != user.PasswordHash)
        {
            throw new UnauthorizedAccessException("�������� email ��� ������ ");;
        }

        // Generate JWT token
        var token = GenerateJwtToken(user);

        return new JwtResponseDto
        {
            Token = token,
            Expires = DateTime.UtcNow.AddDays(7),
            Email = user.Email,
            UserId = user.Id
        };
    }

    public Task<User?> GetUserByIdAsync(Guid id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult(user);
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "default_secret_key_change_in_production");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email)
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
