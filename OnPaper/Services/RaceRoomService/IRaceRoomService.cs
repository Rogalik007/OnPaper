using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.RaceRoomService;

public interface IRaceRoomService
{
    Task SaveRoom(RaceRoomModel room);
    Task<RaceRoomModel?> GetRoom(string? gameId);

    // Состояние хранится отдельно от комнаты: комната после старта не меняется
    // (кроме ConnectionId при переподключении), а состояние переписывается каждым ходом.
    Task SaveState(string gameId, RaceStateModel state);
    Task<RaceStateModel?> GetState(string gameId);
}
