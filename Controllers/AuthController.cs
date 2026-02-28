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
        if (!ModelState.IsValid)
        {
            throw new Exception("����� ������ � ����� ������ ����");
        }

        var result = await _authService.RegisterAsync(registerDto);
        
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        if (!ModelState.IsValid)
        {
            throw new Exception("����� ������ � ����� ������������ �������");
        }

        var result = await _authService.LoginAsync(loginDto);
        
        return Ok(result);
    }
}
