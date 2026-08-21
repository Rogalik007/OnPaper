using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.RaceService;

public interface IRaceService
{
    Task Search(string? userId, string? connectionId);
    Task CancelSearch(string? userId);
    Task<ResultModel<RaceSnapshotModel>> GetRaceState(string? userId, string? gameId, string? connectionId);
    Task<ResultModel<bool>> Move(string? userId, string? gameId, int direction, IHubCallerClients clients);
}
