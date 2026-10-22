using System;
using System.Text.Json;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;
using StackExchange.Redis;

namespace OnPaper.Services.RaceRoomService;

// Гонка хранится в Redis отдельно от комнат GameRoomService: у неё свой лабиринт на всех,
// своя очередь ходов на 2-10 игроков и свои правила выбывания.
public class RaceRoomService(IConnectionMultiplexer redis) : IRaceRoomService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private const string RACE_CONFIG = "race:";
    private const string STATE_CONFIG = ":state";
    private static readonly TimeSpan RaceLifetime = TimeSpan.FromHours(2);

    public async Task SaveRoom(RaceRoomModel room)
        => await _redisDb.StringSetAsync(GetRoomKey(room.GameId), JsonSerializer.Serialize(room), RaceLifetime);

    public async Task<RaceRoomModel?> GetRoom(string? gameId)
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
            return JsonSerializer.Deserialize<RaceRoomModel>(json.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveState(string gameId, RaceStateModel state)
        => await _redisDb.StringSetAsync(GetStateKey(gameId), JsonSerializer.Serialize(state), RaceLifetime);

    public async Task<RaceStateModel?> GetState(string gameId)
    {
        var json = await _redisDb.StringGetAsync(GetStateKey(gameId));

        if (json.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RaceStateModel>(json.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetRoomKey(string gameId) => $"{RACE_CONFIG}{gameId}";
    private static string GetStateKey(string gameId) => $"{RACE_CONFIG}{gameId}{STATE_CONFIG}";
}
