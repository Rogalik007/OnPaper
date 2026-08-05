using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace OnPaper.Services.LobbyService;

public class LobbyService(IConnectionMultiplexer redis) : ILobbyService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private static readonly Random Random = new();
    private const string WaitingPrefix = "waiting:";
    
    public async Task GameSearch(string? userId, string? connectionId, IHubCallerClients clients)
    {
        if (userId == null || connectionId == null)
        {
            return;
        }

        var userKey = $"{WaitingPrefix}{userId}";

        if (await _redisDb.KeyExistsAsync(userKey))
        {
            throw new HubException("You are already in queue");
        }

        var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
        var waitingKeys = server.Keys(pattern: $"{WaitingPrefix}*").ToList();

        if (waitingKeys.Count != 0)
        {
            var randomKey = waitingKeys[Random.Next(waitingKeys.Count)];
            var opponentUserId = randomKey.ToString().Replace(WaitingPrefix, "");
            var opponentConnectionId = await _redisDb.StringGetAsync(randomKey);

            if (opponentConnectionId.IsNullOrEmpty)
            {
                await _redisDb.KeyDeleteAsync(randomKey);
                await GameSearch(userId, connectionId, clients);
                return;
            }

            var gameId = Guid.NewGuid().ToString();

            await _redisDb.KeyDeleteAsync(randomKey);
            await _redisDb.KeyDeleteAsync(userKey);

            await clients.Client(opponentConnectionId!)
                .SendAsync("GameFound", gameId, userId);

            await clients.Caller
                .SendAsync("GameFound", gameId, opponentUserId);
        }
        else
        {
            await _redisDb.StringSetAsync(userKey, connectionId);
            await _redisDb.KeyExpireAsync(userKey, TimeSpan.FromSeconds(60));
        }
    }

    public async Task GameCancelSearch(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }
        
        var userKey = $"{WaitingPrefix}{userId}";
        await _redisDb.KeyDeleteAsync(userKey);
    }
}
