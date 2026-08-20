using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.RatingService;

public interface IRatingService
{
    Task AddWinPoints(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RatingEntry>> GetTopRatings(int count, CancellationToken cancellationToken = default);
}
