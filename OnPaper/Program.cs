using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
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
using StackExchange.Redis;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);
AuthOptions.ISSUER = Environment.GetEnvironmentVariable("JWT_ISSUER")!;
AuthOptions.AUDIENCE = Environment.GetEnvironmentVariable("JWT_AUDIENCE")!;
AuthOptions.KEY = Environment.GetEnvironmentVariable("JWT_KEY")!;

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
        policy.WithOrigins("http://localhost:5173")
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

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<GameHub>("/gameHub");

app.Run();
