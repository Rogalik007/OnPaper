using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;
using OnPaper.Services.AuthService;

namespace OnPaper.Controllers;

[ApiController]
[EnableRateLimiting("auth")]
[Route("[controller]")]
public class AuthController(IAuthService service) : ControllerBase
{
    [HttpPost("/login")]
    public async Task<IActionResult> Login([FromBody]LoginData loginData, CancellationToken cancellationToken)
    {
        var result = await service.Login(loginData.Email, loginData.Password, cancellationToken);
        
        if (!result.IsSuccess)
        {
            return GetActionResultError(result.Error!);
        }
        
        return Ok(new { id = result.Value.Id, token = result.Value.Token });
    }

    [HttpPost("/register")]
    public async Task<IActionResult> Register([FromBody] LoginData loginData, CancellationToken cancellationToken)
    {
        var result = await service.Register(loginData.Email, loginData.Password, cancellationToken);

        if (!result.IsSuccess)
        {
            return GetActionResultError(result.Error!);
        }
        
        return Ok(new { id = result.Value.Id, token = result.Value.Token });
    }
    
    private ObjectResult GetActionResultError(ResultError error)
    {
        return error.Type switch
        {
            ErrorType.BadRequest => BadRequest(error.Message),
            ErrorType.NotFound => NotFound(error.Message),
            ErrorType.Conflict => Conflict(error.Message),
            _ => StatusCode(500, error.Message)
        };
    }
    
    public record LoginData(string Email, string Password);
}
