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

    // Индекс "игрок -> текущая партия": по нему хаб находит игру при обрыве соединения
    Task SetPlayerGame(string userId, string gameId);
    Task<string?> GetPlayerGame(string userId);

    Task SetPlayerLeft(string gameId, PlayerLeftModel left);
    Task<PlayerLeftModel?> GetPlayerLeft(string gameId);
    Task ClearPlayerLeft(string gameId);
}
