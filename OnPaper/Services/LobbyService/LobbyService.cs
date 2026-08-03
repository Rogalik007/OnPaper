using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace OnPaper.Services.LobbyService;

public class LobbyService(IConnectionMultiplexer redis) : ILobbyService
{
    private readonly IDatabase redisDb = redis.GetDatabase();

    private static readonly Random random = new();
    private const string WAITING_PREFIX = "waiting:";

    public async Task<string?> GetValueAsync(string key)
    {
        try
        {
            return await redisDb.StringGetAsync(key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task FindGame(string userId, string connectionId, IHubCallerClients clients)
    {
        if (userId == null || connectionId == null)
        {
            return;
        }

        var userKey = $"{WAITING_PREFIX}{userId}";

        if (await redisDb.KeyExistsAsync(userKey))
        {
            throw new HubException("You are already in queue");
        }

        var server = redisDb.Multiplexer.GetServer(redisDb.Multiplexer.GetEndPoints().First());
        var waitingKeys = server.Keys(pattern: $"{WAITING_PREFIX}*").ToList();

        if (waitingKeys.Any())
        {
            var randomKey = waitingKeys[random.Next(waitingKeys.Count)];
            var opponentUserId = randomKey.ToString().Replace(WAITING_PREFIX, "");
            var opponentConnectionId = await redisDb.StringGetAsync(randomKey);

            if (opponentConnectionId.IsNullOrEmpty)
            {
                await redisDb.KeyDeleteAsync(randomKey);
                await FindGame(userId, connectionId, clients);
                return;
            }

            var gameId = Guid.NewGuid().ToString();

            await redisDb.KeyDeleteAsync(randomKey);
            await redisDb.KeyDeleteAsync(userKey);

            await clients.Client(opponentConnectionId!)
                .SendAsync("GameFound", gameId, userId);

            await clients.Caller
                .SendAsync("GameFound", gameId, opponentUserId);
        }
        else
        {
            await redisDb.StringSetAsync(userKey, connectionId);
            await redisDb.KeyExpireAsync(userKey, TimeSpan.FromSeconds(60));
        }
    }

    public async Task SetValueAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            await _db.StringSetAsync(key, value, expiry, true);
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<bool> KeyExistsAsync(string key) => await _db.KeyExistsAsync(key);
    public async Task<bool> DeleteKeyAsync(string key) => await _db.KeyDeleteAsync(key);
}
