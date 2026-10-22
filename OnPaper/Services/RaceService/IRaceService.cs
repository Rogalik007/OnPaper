using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.RaceService;

public interface IRaceService
{
    Task Search(string? userId, string? connectionId, string? nickname);
    Task CancelSearch(string? userId);
    Task<ResultModel<RaceSnapshotModel>> GetRaceState(string? userId, string? gameId, string? connectionId);
    Task<ResultModel<bool>> Move(string? userId, string? gameId, int direction);
    Task<ResultModel<bool>> Shoot(string? userId, string? gameId, int direction);
    Task<ResultModel<bool>> TurnTimeout(string? userId, string? gameId);
    Task<ResultModel<bool>> Leave(string? userId, string? gameId);
}
