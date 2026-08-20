using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OnPaper.Common.Models.Utils;
using OnPaper.Infrastructure;
using OnPaper.Models;

namespace OnPaper.Services.RatingService;

public class RatingService(OnPaperDbContext dbContext) : IRatingService
{
    private const int WIN_POINTS = 1;

    public async Task AddWinPoints(int userId, CancellationToken cancellationToken = default)
    {
        var rating = await dbContext.Ratings
            .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);

        if (rating == null)
        {
            dbContext.Ratings.Add(new Rating { UserId = userId, Score = WIN_POINTS });
        }
        else
        {
            rating.Score += WIN_POINTS;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RatingEntry>> GetTopRatings(int count, CancellationToken cancellationToken = default)
    {
        // OrderBy должен идти до проекции в record - EF не умеет транслировать сортировку
        // по свойству уже сконструированного RatingEntry.
        return await dbContext.Ratings
            .Join(dbContext.Users, r => r.UserId, u => u.Id, (r, u) => new { u.Email, r.Score })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .Select(x => new RatingEntry(x.Email, x.Score))
            .ToListAsync(cancellationToken);
    }
}
