using System.Linq;

namespace OnPaper.Common.Models.Utils;

public class GameRoomModel
{
    public string GameId { get; set; } = string.Empty;
    public GamePlayerModel[] Players { get; set; } = [];

    public GamePlayerModel? GetPlayer(string userId) => Players.FirstOrDefault(p => p.UserId == userId);
    public GamePlayerModel? GetOpponent(string userId) => Players.FirstOrDefault(p => p.UserId != userId);
}

// ConnectionId нужен, чтобы отправлять сообщения обоим игрокам комнаты,
// UserId - потому что он не меняется при переподключении и по нему лежат лабиринты.
public record GamePlayerModel(string UserId, string ConnectionId);

// Игрок отвалился: если до Deadline он не вернётся, партия закончится
public record PlayerLeftModel(string UserId, System.DateTimeOffset Deadline);
