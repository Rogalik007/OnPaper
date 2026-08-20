using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;
using OnPaper.Services.GameRoomService;

namespace OnPaper.Services.LobbyService;

public class LobbyService(IConnectionMultiplexer redis, IGameRoomService gameRoomService) : ILobbyService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private static readonly Random Random = new();
    private const string WAITING_CONFIG = "waiting:";
    
    public async Task GameSearch(string? userId, string? connectionId, IHubCallerClients clients)
    {
        if (userId == null || connectionId == null)
        {
            return;
        }

        var userKey = $"{WAITING_CONFIG}{userId}";

        if (await _redisDb.KeyExistsAsync(userKey))
        {
            throw new HubException("You are already in queue");
        }

        var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
        var waitingKeys = server.Keys(pattern: $"{WAITING_CONFIG}*").ToList();

        if (waitingKeys.Count != 0)
        {
            var randomKey = waitingKeys[Random.Next(waitingKeys.Count)];
            var opponentUserId = randomKey.ToString().Replace(WAITING_CONFIG, "");
            var opponentConnectionId = await _redisDb.StringGetAsync(randomKey);

            if (opponentConnectionId.IsNullOrEmpty)
            {
                await _redisDb.KeyDeleteAsync(randomKey);
                await GameSearch(userId, connectionId, clients);
                return;
            }

            // DEL атомарен и возвращает true только тому, кто реально удалил ключ.
            // Без этой проверки два одновременных поиска могли выбрать одного и того же
            // соперника и создать для него две разные комнаты.
            if (!await _redisDb.KeyDeleteAsync(randomKey))
            {
                await GameSearch(userId, connectionId, clients);
                return;
            }

            await _redisDb.KeyDeleteAsync(userKey);

            // Комната создаётся до отправки GameFound: игрок сразу после него зовёт SaveMaze,
            // и к этому моменту комната уже должна лежать в redis.
            var room = await gameRoomService.CreateRoom(
                new GamePlayerModel(opponentUserId, opponentConnectionId!),
                new GamePlayerModel(userId, connectionId));

            await clients.Client(opponentConnectionId!)
                .SendAsync("GameFound", room.GameId, userId);

            await clients.Caller
                .SendAsync("GameFound", room.GameId, opponentUserId);
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
        
        var userKey = $"{WAITING_CONFIG}{userId}";
        await _redisDb.KeyDeleteAsync(userKey);
    }
}
