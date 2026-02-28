using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

       
    }
}
