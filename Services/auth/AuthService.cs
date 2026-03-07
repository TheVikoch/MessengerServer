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

        // Use decrypted email for token/response
        var token = GenerateJwtToken(new User { Id = user.Id, Email = registerDto.Email });

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

        // Use decrypted email for token/response
        var token = GenerateJwtToken(new User { Id = user.Id, Email = loginDto.Email });

        return new JwtResponseDto
        {
            Token = token,
            Expires = DateTime.UtcNow.AddDays(7),
            Email = loginDto.Email,
            UserId = user.Id
        };
    }

    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        // Decrypt email before returning
        try
        {
            user.Email = _encryptionService.Decrypt(user.Email);
        }
        catch
        {
            // If decryption fails, return stored value (to avoid throwing for legacy/plain emails)
        }

        return user;
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
