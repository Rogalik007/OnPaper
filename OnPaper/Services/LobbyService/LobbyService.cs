using System;
using System.Text.Json;
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

    public async Task GameSearch(string? userId, string? connectionId, string? nickname, IHubCallerClients clients)
    {
        if (userId == null || connectionId == null || nickname == null)
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
            var opponentDataJson = await _redisDb.StringGetAsync(randomKey);

            if (opponentDataJson.IsNullOrEmpty)
            {
                await _redisDb.KeyDeleteAsync(randomKey);
                await GameSearch(userId, connectionId, nickname, clients);
                return;
            }

            // DEL атомарен и возвращает true только тому, кто реально удалил ключ.
            // Без этой проверки два одновременных поиска могли выбрать одного и того же
            // соперника и создать для него две разные комнаты.
            if (!await _redisDb.KeyDeleteAsync(randomKey))
            {
                await GameSearch(userId, connectionId, nickname, clients);
                return;
            }

            await _redisDb.KeyDeleteAsync(userKey);

            var opponentData = JsonSerializer.Deserialize<WaitingPlayerData>(opponentDataJson.ToString())!;

            // Комната создаётся до отправки GameFound: игрок сразу после него зовёт SaveMaze,
            // и к этому моменту комната уже должна лежать в redis.
            var room = await gameRoomService.CreateRoom(
                new GamePlayerModel(opponentUserId, opponentData.ConnectionId, opponentData.Nickname),
                new GamePlayerModel(userId, connectionId, nickname));

            await clients.Client(opponentData.ConnectionId)
                .SendAsync("GameFound", room.GameId, userId, nickname);

            await clients.Caller
                .SendAsync("GameFound", room.GameId, opponentUserId, opponentData.Nickname);
        }
        else
        {
            var data = JsonSerializer.Serialize(new WaitingPlayerData(connectionId, nickname));
            await _redisDb.StringSetAsync(userKey, data);
            await _redisDb.KeyExpireAsync(userKey, TimeSpan.FromSeconds(60));
        }
    }

    private record WaitingPlayerData(string ConnectionId, string Nickname);

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
