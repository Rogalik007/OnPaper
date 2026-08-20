using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnPaper.Services.UsersService;
using OnPaper.Common.Models.Utils;
using OnPaper.Common.Enums;

namespace OnPaper.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public class UsersController(IUsersService service) : ControllerBase
{
    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetUser([FromRoute] int userId, CancellationToken cancellationToken)
    {
        // Профиль отдаётся только его владельцу - иначе любой авторизованный
        // пользователь мог бы перебором id вытащить чужие email.
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (callerId != userId.ToString())
        {
            return Forbid();
        }

        var result = await service.GetUser(userId, cancellationToken);

        if (!result.IsSuccess)
        {
            return GetActionResultError(result.Error!);
        }

        return Ok(new { email = result.Value!.Email });
    }

    /*[HttpPatch("{userId:int}")]
    public async Task<IActionResult> PatchUser([FromRoute] int userId, [FromBody] UserData userData, CancellationToken cancellationToken)
    {
        
    }*/
    
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

    public record UserData(string Email, string Password);
}
