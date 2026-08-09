using System;
using System.Text.Json;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;
using StackExchange.Redis;

namespace OnPaper.Services.GameRoomService;

public class GameRoomService(IConnectionMultiplexer redis) : IGameRoomService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private const string GAME_CONFIG = "game:";
    private const string STATE_CONFIG = ":state";

    // В отличие от ключей ожидания в лобби, комната живёт всю партию, а не 60 секунд.
    private static readonly TimeSpan GameLifetime = TimeSpan.FromHours(2);

    public async Task<GameRoomModel> CreateRoom(GamePlayerModel first, GamePlayerModel second)
    {
        var room = new GameRoomModel
        {
            GameId = Guid.NewGuid().ToString(),
            Players = [first, second]
        };

        await _redisDb.StringSetAsync(GetRoomKey(room.GameId), JsonSerializer.Serialize(room), GameLifetime);

        return room;
    }

    // Комната переживает переподключение, поэтому ConnectionId в ней приходится обновлять
    public async Task SaveRoom(GameRoomModel room)
        => await _redisDb.StringSetAsync(GetRoomKey(room.GameId), JsonSerializer.Serialize(room), GameLifetime);

    public async Task<GameRoomModel?> GetRoom(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId))
        {
            return null;
        }

        var json = await _redisDb.StringGetAsync(GetRoomKey(gameId));

        if (json.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GameRoomModel>(json.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Если оба игрока сохранили лабиринт одновременно, партия должна начаться один раз:
    // ключ ставится через When.NotExists, поэтому true получит только первый вызов.
    public async Task<bool> TryStartGame(string gameId, GameStateModel state)
        => await _redisDb.StringSetAsync(
            GetStateKey(gameId), JsonSerializer.Serialize(state), GameLifetime, when: When.NotExists);

    public async Task<GameStateModel?> GetState(string gameId)
    {
        var json = await _redisDb.StringGetAsync(GetStateKey(gameId));

        if (json.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GameStateModel>(json.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveState(string gameId, GameStateModel state)
        => await _redisDb.StringSetAsync(GetStateKey(gameId), JsonSerializer.Serialize(state), GameLifetime);

    private static string GetRoomKey(string gameId) => $"{GAME_CONFIG}{gameId}";
    private static string GetStateKey(string gameId) => $"{GAME_CONFIG}{gameId}{STATE_CONFIG}";
}
