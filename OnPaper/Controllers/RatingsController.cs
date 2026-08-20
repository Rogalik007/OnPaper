using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OnPaper.Services.RatingService;

namespace OnPaper.Controllers;

[ApiController]
[Route("[controller]")]
public class RatingsController(IRatingService service) : ControllerBase
{
    private const int TOP_COUNT = 10;

    [HttpGet("top")]
    public async Task<IActionResult> GetTop(CancellationToken cancellationToken)
    {
        var top = await service.GetTopRatings(TOP_COUNT, cancellationToken);

        return Ok(top);
    }
}
