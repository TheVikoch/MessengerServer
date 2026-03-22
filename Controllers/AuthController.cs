using MessengerServer;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.auth;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto registerDto)
    {
        // это походу вообще никогда не будет работать
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _authService.RegisterAsync(registerDto);
            return Ok(result);
        }
        catch (UserAlreadyExistsException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DisplayNameAlreadyExistsException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        // это походу вообще никогда не будет работать
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.LoginAsync(loginDto);

        return Ok(result);
    }

    [HttpGet("sessions/{userId:guid}")]
    public async Task<IActionResult> GetSessions(Guid userId)
    {
        var sessions = await _authService.GetSessionsForUserAsync(userId);
        return Ok(sessions);
    }

    public class RevokeSessionDto { public Guid SessionId { get; set; } public Guid UserId { get; set; } }

    [HttpPost("sessions/revoke")]
    public async Task<IActionResult> RevokeSession(RevokeSessionDto dto)
    {
        await _authService.RevokeSessionAsync(dto.SessionId, dto.UserId);
        return Ok(new { message = "Session revoked" });
    }
}
