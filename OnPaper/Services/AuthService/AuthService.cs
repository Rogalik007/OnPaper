using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OnPaper.Common.Models.Config;
using OnPaper.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using OnPaper.Infrastructure;
using OnPaper.Common.Models.Utils;
using OnPaper.Common.Enums;
using OnPaper.Services.EmailService;

namespace OnPaper.Services.AuthService;

public class AuthService(OnPaperDbContext dbContext, IEmailSender emailSender) : IAuthService
{
    // Ссылка на подтверждение ведёт на фронтенд, а не на бэкенд: там страница сама
    // дёргает POST /confirm-email и показывает результат.
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromHours(24);

    // Считается один раз при старте и используется, когда пользователя не существует,
    // чтобы Login тратил на bcrypt одинаковое время независимо от того, есть такой email или нет.
    // Без этого по времени ответа можно было бы отличать существующие email от несуществующих.
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    public async Task<ResultModel<AuthModel>> Login(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ResultModel.Fail<AuthModel>("Email is required", ErrorType.BadRequest);
        }

        User? user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user?.Password ?? DummyPasswordHash);

        if (user == null || !isPasswordValid)
        {
            return ResultModel.Fail<AuthModel>("Invalid email or password", ErrorType.BadRequest);
        }

        if (!user.EmailConfirmed)
        {
            return ResultModel.Fail<AuthModel>(
                "Please confirm your email before logging in", ErrorType.BadRequest);
        }

        string token = GenerateToken(user.Id.ToString(), user.Email);

        return ResultModel.Ok(new AuthModel(token, user.Id));
    }

    public async Task<ResultModel<int>> Register(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ResultModel.Fail<int>("Email is required", ErrorType.BadRequest);
        }

        if (await dbContext.Users.AnyAsync(p => p.Email == email, cancellationToken))
        {
            return ResultModel.Fail<int>("Email already exists", ErrorType.Conflict);
        }

        if (password.Length < 8)
        {
            return ResultModel.Fail<int>("Password must be at least 8 characters long", ErrorType.BadRequest);
        }

        if (password.Length > 64)
        {
            return ResultModel.Fail<int>("Password must be no more than 64 characters long", ErrorType.BadRequest);
        }

        var confirmationToken = GenerateConfirmationToken();

        var user = new User()
        {
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            EmailConfirmed = false,
            EmailConfirmationToken = confirmationToken,
            EmailConfirmationTokenExpiresAt = DateTimeOffset.UtcNow.Add(ConfirmationLifetime)
        };

        await dbContext.Users.AddAsync(user, cancellationToken);

        var isSuccess = false;
        try
        {
            isSuccess = await dbContext.SaveChangesAsync(cancellationToken) > 0;
        }
        catch
        {
            isSuccess = false;
        }

        if (!isSuccess)
        {
            return ResultModel.Fail<int>("Internal server error", ErrorType.ServerError);
        }

        var frontendUrl = (Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "https://localhost:8080")
            .TrimEnd('/');
        var confirmationLink = $"{frontendUrl}/confirm-email?token={confirmationToken}";

        await emailSender.SendEmailConfirmationAsync(user.Email, confirmationLink, cancellationToken);

        return ResultModel.Ok(user.Id);
    }

    public async Task<ResultModel<bool>> ConfirmEmail(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ResultModel.Fail<bool>("Confirmation token is required", ErrorType.BadRequest);
        }

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.EmailConfirmationToken == token, cancellationToken);

        if (user == null)
        {
            return ResultModel.Fail<bool>("Invalid or already used confirmation link", ErrorType.BadRequest);
        }

        if (user.EmailConfirmationTokenExpiresAt is null || user.EmailConfirmationTokenExpiresAt < DateTimeOffset.UtcNow)
        {
            return ResultModel.Fail<bool>("Confirmation link has expired", ErrorType.BadRequest);
        }

        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiresAt = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ResultModel.Ok(true);
    }

    private static string GenerateConfirmationToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string GenerateToken(string id, string username)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Email, username)
        };
        var jwt = new JwtSecurityToken(
            issuer: AuthOptions.ISSUER,
            audience: AuthOptions.AUDIENCE,
            claims: claims,
            expires: DateTime.UtcNow.Add(TimeSpan.FromHours(24)),
            signingCredentials: new SigningCredentials(AuthOptions.GetSymmetricSecurityKey(), SecurityAlgorithms.HmacSha256));
        
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}