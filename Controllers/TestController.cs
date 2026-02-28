using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { message = "pong" });
        }

        [HttpGet("error")]
        public IActionResult ThrowError()
        {
            throw new Exception("Тестовая ошибка");
        }
    }
}

