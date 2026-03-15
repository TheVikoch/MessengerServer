using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MessengerServer.Middlewares;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Отключает автоматический возврат 400 при невалидном ModelState
        options.SuppressModelStateInvalidFilter = true;

        // Отключает автоматическое маппинг клиентских ошибок
        options.SuppressMapClientErrors = true;
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL") ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Port=5432;Database=MessengerDB;Username=postgres;Password=postgres"));

builder.Services.AddScoped<MessengerServer.Services.auth.IAuthService, MessengerServer.Services.auth.AuthService>();
builder.Services.AddScoped<MessengerServer.Services.encryption.IEncryptionService, MessengerServer.Services.encryption.EncryptionService>();
builder.Services.AddScoped<MessengerServer.Services.chat.IChatService, MessengerServer.Services.chat.ChatService>();

// Register MessageService (core implementation)
builder.Services.AddScoped<MessengerServer.Services.messages.IMessageService, MessengerServer.Services.messages.MessageService>();

// Register WebSocket Notifier
builder.Services.AddScoped<MessengerServer.Services.websocket.IWebSocketNotifier, MessengerServer.Services.websocket.WebSocketNotifier>();

// Add SignalR
builder.Services.AddSignalR();

var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "default_secret_key_change_in_production");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "MessengerServer",
        ValidAudience = jwtSettings["Audience"] ?? "MessengerClient",
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<MessengerServer.Services.auth.IAuthService>();
            
            // Get sessionId from token claims
            var sessionIdClaim = context.Principal?.FindFirst("sessionId");
            var userIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            
            if (sessionIdClaim == null || userIdClaim == null)
            {
                context.Fail("Session identifier missing from token");
                return;
            }

            if (!Guid.TryParse(sessionIdClaim.Value, out var sessionId) || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                context.Fail("Invalid session or user identifier format");
                return;
            }

            // Validate session in database
            var isValid = await authService.ValidateSessionAsync(sessionId, userId);
            if (!isValid)
            {
                context.Fail("Session is invalid, revoked, or expired");
            }
        }
    };
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100, // запросов в минуту (window)
                QueueLimit = 0, // запросов в очереди если >PermitLimit
                Window = TimeSpan.FromMinutes(1) // окно за которое считается лимит
            }));
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});


var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Используем миграции для создания/обновления схемы БД.
    db.Database.Migrate();
}

app.UseRateLimiter();

app.UseHttpsRedirection(); // ?
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map SignalR Hub
app.MapHub<MessengerServer.Hubs.MessengerHub>("/messengerHub");

// Configure SignalR to use JWT authentication
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/messengerHub"))
    {
        var token = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token))
        {
            context.Request.Headers["Authorization"] = $"Bearer {token}";
        }
    }
    await next();
});

app.Run();