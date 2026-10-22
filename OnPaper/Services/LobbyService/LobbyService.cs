using System;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;
using OnPaper.Services.GameRoomService;

namespace OnPaper.Services.LobbyService;

public class LobbyService(IConnectionMultiplexer redis, IGameRoomService gameRoomService) : ILobbyService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private static readonly Random Random = new();
    private const string WAITING_CONFIG = "waiting:";

    // Проверка очереди и постановка в неё - два отдельных запроса к redis. Без блокировки два игрока,
    // нажавшие Play одновременно, оба видели пустую очередь, оба вставали ждать и не находили
    // друг друга. Блокировка процессная - как и очередь гонки, матчмейкинг рассчитан на один инстанс.
    private static readonly SemaphoreSlim QueueLock = new(1, 1);

    public async Task GameSearch(string? userId, string? connectionId, string? nickname, IHubCallerClients clients)
    {
        if (userId == null || connectionId == null || nickname == null)
        {
            return;
        }

        await QueueLock.WaitAsync();

        try
        {
            await GameSearchLocked(userId, connectionId, nickname, clients);
        }
        finally
        {
            QueueLock.Release();
        }
    }

    private async Task GameSearchLocked(string userId, string connectionId, string nickname,
        IHubCallerClients clients)
    {
        var userKey = $"{WAITING_CONFIG}{userId}";

        if (await _redisDb.KeyExistsAsync(userKey))
        {
            throw new HubException("You are already in queue");
        }

        var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());

        // Под блокировкой очередь меняет только этот вызов, поэтому битые записи
        // можно просто выкинуть и взять следующую, без повторного поиска
        foreach (var randomKey in server.Keys(pattern: $"{WAITING_CONFIG}*").OrderBy(_ => Random.Next()))
        {
            var opponentDataJson = await _redisDb.StringGetDeleteAsync(randomKey);

            if (opponentDataJson.IsNullOrEmpty)
            {
                continue;
            }

            var opponentUserId = randomKey.ToString().Replace(WAITING_CONFIG, "");
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

            return;
        }

        // Без TTL: раньше ключ молча истекал через 60 секунд, а клиент так и висел в "Searching...".
        // Из очереди игрок уходит сам (GameCancelSearch) или при обрыве соединения (OnDisconnectedAsync),
        // а записи, пережившие перезапуск сервера, чистит ClearQueue.
        var data = JsonSerializer.Serialize(new WaitingPlayerData(connectionId, nickname));
        await _redisDb.StringSetAsync(userKey, data);
    }

    private record WaitingPlayerData(string ConnectionId, string Nickname);

    public async Task GameCancelSearch(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        // Под той же блокировкой: иначе отмена могла проскочить посреди подбора пары
        await QueueLock.WaitAsync();

        try
        {
            await _redisDb.KeyDeleteAsync($"{WAITING_CONFIG}{userId}");
        }
        finally
        {
            QueueLock.Release();
        }
    }

    // Соединения не переживают перезапуск сервера, а записи очереди в redis - переживают
    // и превратились бы в "призраков", с которыми матчило бы живых игроков.
    public async Task ClearQueue()
    {
        var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());

        foreach (var key in server.Keys(pattern: $"{WAITING_CONFIG}*"))
        {
            await _redisDb.KeyDeleteAsync(key);
        }
    }
}
