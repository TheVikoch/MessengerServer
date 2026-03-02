using MessengerServer.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace MessengerServer.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger)
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
                if (httpContext.Response.HasStarted)
                {
                    _logger.LogWarning("Response already started, cannot write error");
                    throw;
                }

                await HandleExceptionAsync(httpContext,ex);
            }
        }

        // То, что отправляем в виде искоючения 
        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            var traceId = context.TraceIdentifier;

            _logger.LogError(ex, "Unhandled exception. TraceId: {TraceId}", traceId);

            var (status, message) = MapException(ex);

            ErrorDto error = new()
            {
                Message = message,
                StatusCode = (int)status,
                TraceId = traceId
            };

            HttpResponse response = context.Response;
            response.ContentType = "application/json";
            response.StatusCode = (int)status;

            await response.WriteAsJsonAsync(error);
        }

        // В будущем написать свои исключения
        private static (HttpStatusCode status, string message) MapException(Exception ex)
        {
            return ex switch
            {
                ArgumentNullException => (HttpStatusCode.BadRequest, "Некорректные данные"),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Отказано в доступе"),
                SmtpException => (HttpStatusCode.InternalServerError, "Ошибка отправки сообщения"),
                KeyNotFoundException => (HttpStatusCode.NotFound, "Не найдено"),
                UserAlreadyExistsException => (HttpStatusCode.Conflict, ex.Message),
                InvalidOperationException => (HttpStatusCode.Conflict, ex.Message),
                ValidationException => (HttpStatusCode.BadRequest, "Неверные данные запроса"),

                //NotFoundException => (HttpStatusCode.NotFound, "Ресурс не найден"),
                //DbUpdateException => (HttpStatusCode.Conflict, "Ошибка сохранения данных"),

                _ => (HttpStatusCode.InternalServerError, "Ошибка сервера (хуй знает какая)")
            };
        }
    }
}