using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MessengerServer.Data;
using MessengerServer.Middlewares;
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
builder.Services.AddScoped<MessengerServer.Services.profile.IUserProfileService, MessengerServer.Services.profile.UserProfileService>();
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
            var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier);

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
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(10)
});

app.MapControllers();

app.Map("/stream-transfer/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket request expected");
        return;
    }

    if (context.User?.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var transferIdRaw = context.Request.Query["transferId"].ToString();
    var roleRaw = context.Request.Query["role"].ToString();
    var laneRaw = context.Request.Query["lane"].ToString();
    if (!Guid.TryParse(transferIdRaw, out var transferId) ||
        !TryParseStreamTransferRole(roleRaw, out var role) ||
        !int.TryParse(laneRaw, out var lane) ||
        lane < 0)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Invalid transfer websocket parameters");
        return;
    }

    var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirst("sub")
        ?? context.User.FindFirst("userId");
    if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var transferService = context.RequestServices.GetRequiredService<MessengerServer.Services.stream.IStreamTransferService>();
    using var socket = await context.WebSockets.AcceptWebSocketAsync();

    try
    {
        await transferService.AttachSocketAsync(transferId, userId, role, lane, socket, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // Normal shutdown / client disconnect.
    }
    catch (KeyNotFoundException)
    {
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "transfer_not_found", CancellationToken.None);
        }
    }
    catch (UnauthorizedAccessException)
    {
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "unauthorized", CancellationToken.None);
        }
    }
    catch (InvalidOperationException ex)
    {
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, ex.Message, CancellationToken.None);
        }
    }
    catch (Exception)
    {
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "stream_transfer_error", CancellationToken.None);
        }
    }
});

// Map SignalR Hub
app.MapHub<MessengerServer.Hubs.MessengerHub>("/messengerHub");

app.Run();

static bool TryParseStreamTransferRole(string rawRole, out MessengerServer.Services.stream.StreamTransferSocketRole role)
{
    if (string.Equals(rawRole, "sender", StringComparison.OrdinalIgnoreCase))
    {
        role = MessengerServer.Services.stream.StreamTransferSocketRole.Sender;
        return true;
    }

    if (string.Equals(rawRole, "receiver", StringComparison.OrdinalIgnoreCase))
    {
        role = MessengerServer.Services.stream.StreamTransferSocketRole.Receiver;
        return true;
    }

    role = default;
    return false;
}
