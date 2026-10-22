using System;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OnPaper.Common.Models.Config;
using OnPaper.Hubs;
using OnPaper.Infrastructure;
using OnPaper.Services.AuthService;
using OnPaper.Services.LobbyService;
using OnPaper.Services.UsersService;
using OnPaper.Services.GameRoomService;
using OnPaper.Services.GameTreasureService;
using OnPaper.Services.RatingService;
using OnPaper.Services.ChatService;
using OnPaper.Services.RaceRoomService;
using OnPaper.Services.RaceService;
using OnPaper.Services.EmailService;
using StackExchange.Redis;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
AuthOptions.ISSUER = Environment.GetEnvironmentVariable("JWT_ISSUER")!;
AuthOptions.AUDIENCE = Environment.GetEnvironmentVariable("JWT_AUDIENCE")!;
AuthOptions.KEY = Environment.GetEnvironmentVariable("JWT_KEY")!;

// Явный биндинг на HTTPS (а не launchSettings.json), чтобы порт и сертификат были одинаковыми
// независимо от того, как запущен процесс - dotnet run, systemd, докер и т.д. Сертификат
// самоподписанный, поэтому в браузере при первом заходе нужно будет подтвердить исключение.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5244, listenOptions =>
        listenOptions.UseHttps(
            Path.Combine(builder.Environment.ContentRootPath, "https", "devcert.pfx"),
            Environment.GetEnvironmentVariable("HTTPS_CERT_PASSWORD")));
});

var connectionStringBuilder = new NpgsqlConnectionStringBuilder
{
    Host = Environment.GetEnvironmentVariable("DB_HOST"),
    Port = Int32.Parse(Environment.GetEnvironmentVariable("DB_PORT")!),
    Database = Environment.GetEnvironmentVariable("DB_NAME"),
    Username = Environment.GetEnvironmentVariable("DB_USER"),
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD"),
    Pooling = true,
    MinPoolSize = 1,
    MaxPoolSize = 20,
    CommandTimeout = 30,
    SslMode = SslMode.Prefer,
    Timeout = 15
};

builder.Services.AddDbContext<OnPaperDbContext>(options =>
    options.UseNpgsql(connectionStringBuilder.ConnectionString));

var conf = new ConfigurationOptions {
    EndPoints = { Environment.GetEnvironmentVariable("REDIS_HOST") + ":" + Environment.GetEnvironmentVariable("REDIS_PORT") }
};

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(conf));

builder.Services.AddScoped<IGameRoomService, GameRoomService>();
builder.Services.AddScoped<ILobbyService, LobbyService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUsersService, UsersService>();
builder.Services.AddScoped<IGameTreasureService, GameTreasureService>();
builder.Services.AddScoped<IRatingService, RatingService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// Синглтоны: очередь матчмейкинга гонки живёт в памяти процесса, а не в Redis,
// поэтому и хранящий её сервис, и обёртка над комнатами должны быть одним экземпляром.
builder.Services.AddSingleton<IRaceRoomService, RaceRoomService>();
builder.Services.AddSingleton<IRaceService, RaceService>();

builder.Services.AddControllers();

// По умолчанию хаб отдаёт свойства в PascalCase - клиенту на TS удобнее camelCase.
// Входящие сообщения разбираются без учёта регистра, поэтому их это не ломает.
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://localhost:8080", "https://185.21.11.60:8080")
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = AuthOptions.ISSUER,
            ValidAudience = AuthOptions.AUDIENCE,
            IssuerSigningKey = AuthOptions.GetSymmetricSecurityKey()
        };
        
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/gameHub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Ограничивает перебор пароля и массовую регистрацию: без лимита /login и /register
// можно было дёргать сколько угодно раз в секунду.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ILobbyService>().ClearQueue();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapHub<GameHub>("/gameHub");

app.Run();
