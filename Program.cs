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
// Configure EF Core (PostgreSQL)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL") ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Port=5432;Database=MessengerDB;Username=postgres;Password=postgres"));

builder.Services.AddScoped<MessengerServer.Services.auth.IAuthService, MessengerServer.Services.auth.AuthService>();
builder.Services.AddSingleton<MessengerServer.Services.encryption.IEncryptionService, MessengerServer.Services.encryption.EncryptionService>();

// Configure JWT authentication
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
});

// Rate Limiting
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

// Ensure database and tables are created without migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseRateLimiter();

app.UseHttpsRedirection(); // ?
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
    
app.Run();