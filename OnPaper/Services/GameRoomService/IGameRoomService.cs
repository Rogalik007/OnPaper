using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.GameRoomService;

public interface IGameRoomService
{
    Task<GameRoomModel> CreateRoom(GamePlayerModel first, GamePlayerModel second);
    Task<GameRoomModel?> GetRoom(string? gameId);
    Task SaveRoom(GameRoomModel room);
    Task<bool> TryStartGame(string gameId, GameStateModel state);
    Task<GameStateModel?> GetState(string gameId);
    Task SaveState(string gameId, GameStateModel state);
}
