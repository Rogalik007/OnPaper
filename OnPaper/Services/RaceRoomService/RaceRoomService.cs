using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;
using StackExchange.Redis;

namespace OnPaper.Services.RaceRoomService;

// Комната гонки хранится в Redis отдельно от комнат GameRoomService: у неё нет ни ходов,
// ни победителя - только состав игроков и их позиции на общем лабиринте.
public class RaceRoomService(IConnectionMultiplexer redis) : IRaceRoomService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private const string RACE_CONFIG = "race:";
    private const string POS_CONFIG = ":pos";
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

    public async Task InitPositions(string gameId, IReadOnlyDictionary<string, int> positions)
    {
        var entries = positions.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray();

        await _redisDb.HashSetAsync(GetPosKey(gameId), entries);
        await _redisDb.KeyExpireAsync(GetPosKey(gameId), RaceLifetime);
    }

    public async Task<int?> GetPosition(string gameId, string userId)
    {
        var value = await _redisDb.HashGetAsync(GetPosKey(gameId), userId);

        return value.IsNullOrEmpty ? null : (int)value;
    }

    public async Task SetPosition(string gameId, string userId, int position)
        => await _redisDb.HashSetAsync(GetPosKey(gameId), userId, position);

    public async Task<Dictionary<string, int>> GetPositions(string gameId)
    {
        var entries = await _redisDb.HashGetAllAsync(GetPosKey(gameId));

        return entries.ToDictionary(e => e.Name.ToString(), e => (int)e.Value);
    }

    private static string GetRoomKey(string gameId) => $"{RACE_CONFIG}{gameId}";
    private static string GetPosKey(string gameId) => $"{RACE_CONFIG}{gameId}{POS_CONFIG}";
}
