using MessengerServer.Models.DTOs;
using System.Net;
using System.Text.Json;

namespace MessengerServer.Middlewares
{
    public class ExeptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExeptionHandlingMiddleware> _logger;

        public ExeptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExeptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        // Здесь ловим все исключения, которые нам нужны
        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                await HandleExeptionAsync(httpContext,
                    ex.Message,
                    HttpStatusCode.Conflict,
                    "Конфликт");
                return;
            }
        }

        // То, что отправляем в виде искоючения 
        private async Task HandleExeptionAsync(
            HttpContext context,
            string exMsg,
            HttpStatusCode httpStatusCode,
            string message)
        {
            _logger.LogError(exMsg);

            HttpResponse response = context.Response;
            response.ContentType = "application/json";
            response.StatusCode = (int)httpStatusCode;

            ErrorDto errorDto = new()
            {
                Message = message,
                StatusCode = (int)httpStatusCode
            };

            await response.WriteAsJsonAsync(errorDto);
        }
    }
}
