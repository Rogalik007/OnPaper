using System;
using System.Collections.Generic;
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

namespace OnPaper.Services.AuthService;

public class AuthService(OnPaperDbContext dbContext) : IAuthService
{
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

        string token = GenerateToken(user.Id.ToString(), user.Email);

        return ResultModel.Ok(new AuthModel(token, user.Id));
    }

    public async Task<ResultModel<AuthModel>> Register(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ResultModel.Fail<AuthModel>("Email is required", ErrorType.BadRequest);
        }

        if (await dbContext.Users.AnyAsync(p => p.Email == email, cancellationToken))
        {
            return ResultModel.Fail<AuthModel>("Email already exists", ErrorType.Conflict);
        }

        if (password.Length < 8)
        {
            return ResultModel.Fail<AuthModel>("Password must be at least 8 characters long", ErrorType.BadRequest);
        }

        if (password.Length > 64)
        {
            return ResultModel.Fail<AuthModel>("Password must be no more than 64 characters long", ErrorType.BadRequest);
        }

        var user = new User()
        {
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(password)
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
            return ResultModel.Fail<AuthModel>("Internal server error", ErrorType.ServerError);
        }
        
        var token = GenerateToken(user.Id.ToString(), user.Email);
        return ResultModel.Ok(new AuthModel(token, user.Id));
    }

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