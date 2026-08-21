using System.Collections.Generic;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.RaceRoomService;

public interface IRaceRoomService
{
    Task SaveRoom(RaceRoomModel room);
    Task<RaceRoomModel?> GetRoom(string? gameId);

    // Позиции живут отдельно от комнаты, в хэше: каждый игрок пишет только своё поле,
    // поэтому одновременные ходы разных игроков не затирают друг друга.
    Task InitPositions(string gameId, IReadOnlyDictionary<string, int> positions);
    Task<int?> GetPosition(string gameId, string userId);
    Task SetPosition(string gameId, string userId, int position);
    Task<Dictionary<string, int>> GetPositions(string gameId);
}
