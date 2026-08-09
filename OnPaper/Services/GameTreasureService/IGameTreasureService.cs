using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.GameTreasureService;

public interface IGameTreasureService
{
    int[] GetMazeSize();

    Task<ResultModel<GameSnapshotModel>> GetGameState(string? userId, string? gameId, string? connectionId,
        IHubCallerClients clients);

    Task<ResultModel<bool>> SaveMaze(string? userId, string? gameId, MazeModel? maze, IHubCallerClients clients);
    Task<MazeModel?> GetMaze(string? gameId, string? userId);

    Task<ResultModel<bool>> Move(string? userId, string? gameId, int direction, IHubCallerClients clients);
    Task<ResultModel<bool>> TurnTimeout(string? userId, string? gameId, IHubCallerClients clients);

    Task PlayerLeft(string? userId, IHubCallerClients clients);
    Task<ResultModel<bool>> CheckOpponentLeft(string? userId, string? gameId, IHubCallerClients clients);
}
