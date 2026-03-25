using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MessengerServer.Middlewares;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Data;
using MessengerServer.Services.storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // РћС‚РєР»СЋС‡Р°РµС‚ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРёР№ РІРѕР·РІСЂР°С‚ 400 РїСЂРё РЅРµРІР°Р»РёРґРЅРѕРј ModelState
        options.SuppressModelStateInvalidFilter = true;

        // РћС‚РєР»СЋС‡Р°РµС‚ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРѕРµ РјР°РїРїРёРЅРі РєР»РёРµРЅС‚СЃРєРёС… РѕС€РёР±РѕРє
        options.SuppressMapClientErrors = true;
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL") ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Port=5432;Database=MessengerDB;Username=postgres;Password=postgres"));

builder.Services.Configure<S3Options>(builder.Configuration.GetSection("S3"));

builder.Services.AddScoped<MessengerServer.Services.auth.IAuthService, MessengerServer.Services.auth.AuthService>();
builder.Services.AddScoped<MessengerServer.Services.encryption.IEncryptionService, MessengerServer.Services.encryption.EncryptionService>();
builder.Services.AddScoped<MessengerServer.Services.chat.IChatService, MessengerServer.Services.chat.ChatService>();
builder.Services.AddScoped<MessengerServer.Services.storage.IStorageService, MessengerServer.Services.storage.S3StorageService>();
builder.Services.AddScoped<MessengerServer.Services.media.IMediaService, MessengerServer.Services.media.MediaService>();
builder.Services.AddScoped<MessengerServer.Services.stream.IStreamInviteService, MessengerServer.Services.stream.StreamInviteService>();
builder.Services.Configure<MessengerServer.Services.stream.StreamTransferOptions>(
    builder.Configuration.GetSection("StreamTransfer"));
builder.Services.AddSingleton<MessengerServer.Services.stream.StreamTransferService>();
builder.Services.AddSingleton<MessengerServer.Services.stream.IStreamTransferService>(sp =>
    sp.GetRequiredService<MessengerServer.Services.stream.StreamTransferService>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<MessengerServer.Services.stream.StreamTransferService>());

// Register MessageService (core implementation)
builder.Services.AddScoped<MessengerServer.Services.messages.IMessageService, MessengerServer.Services.messages.MessageService>();

// Register WebSocket Notifier
builder.Services.AddScoped<MessengerServer.Services.websocket.IWebSocketNotifier, MessengerServer.Services.websocket.WebSocketNotifier>();

// Add SignalR
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 16 * 1024 * 1024;
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

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
        OnMessageReceived = context =>
        {
            // Allow SignalR to pass JWT via query string (?access_token=...) for WebSocket connections.
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/messengerHub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
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
                PermitLimit = 100, // Р·Р°РїСЂРѕСЃРѕРІ РІ РјРёРЅСѓС‚Сѓ (window)
                QueueLimit = 0, // Р·Р°РїСЂРѕСЃРѕРІ РІ РѕС‡РµСЂРµРґРё РµСЃР»Рё >PermitLimit
                Window = TimeSpan.FromMinutes(1) // РѕРєРЅРѕ Р·Р° РєРѕС‚РѕСЂРѕРµ СЃС‡РёС‚Р°РµС‚СЃСЏ Р»РёРјРёС‚
            }));
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});


var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // РСЃРїРѕР»СЊР·СѓРµРј РјРёРіСЂР°С†РёРё РґР»СЏ СЃРѕР·РґР°РЅРёСЏ/РѕР±РЅРѕРІР»РµРЅРёСЏ СЃС…РµРјС‹ Р‘Р”.
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

app.Run();

