using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;
using OnPaper.Services.GameRoomService;
using StackExchange.Redis;

namespace OnPaper.Services.ChatService;

public class ChatService(IConnectionMultiplexer redis, IGameRoomService gameRoomService) : IChatService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    public const int MAX_MESSAGE_LENGTH = 64;

    // Старые сообщения не нужны бесконечно - партия живёт максимум GameLifetime, столько же и чат
    private const int MAX_HISTORY = 200;
    private const string GAME_CONFIG = "game:";
    private const string CHAT_CONFIG = ":chat";
    private const string COOLDOWN_CONFIG = "chat-cooldown:";
    private static readonly TimeSpan ChatLifetime = TimeSpan.FromHours(2);

    // Не даёт заспамить чат: SignalR-хаб не покрывается HTTP rate limiter'ом,
    // поэтому лимит на частоту сообщений приходится ставить здесь.
    private static readonly TimeSpan ChatCooldown = TimeSpan.FromMilliseconds(500);

    public async Task<ResultModel<ChatMessageModel>> SendMessage(string? userId, string? gameId, string? text,
        IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<ChatMessageModel>("User is not authorized", ErrorType.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return ResultModel.Fail<ChatMessageModel>("Message is empty", ErrorType.BadRequest);
        }

        text = text.Trim();

        if (text.Length > MAX_MESSAGE_LENGTH)
        {
            return ResultModel.Fail<ChatMessageModel>(
                $"Message must be no more than {MAX_MESSAGE_LENGTH} characters long", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);

        if (room == null || room.GetPlayer(userId) == null)
        {
            return ResultModel.Fail<ChatMessageModel>("Game not found", ErrorType.NotFound);
        }

        // When.NotExists делает лимит атомарным: пройдёт только тот вызов, что первым
        // поставил ключ - конкурентные отправки не смогут проскочить друг перед другом.
        var canSend = await _redisDb.StringSetAsync(
            GetCooldownKey(userId), "1", ChatCooldown, when: When.NotExists);

        if (!canSend)
        {
            return ResultModel.Fail<ChatMessageModel>("You are sending messages too fast", ErrorType.BadRequest);
        }

        var message = new ChatMessageModel(userId, text, DateTimeOffset.UtcNow);
        var key = GetChatKey(room.GameId);

        await _redisDb.ListRightPushAsync(key, JsonSerializer.Serialize(message));
        await _redisDb.ListTrimAsync(key, -MAX_HISTORY, -1);
        await _redisDb.KeyExpireAsync(key, ChatLifetime);

        foreach (var player in room.Players)
        {
            await clients.User(player.UserId).SendAsync("ChatMessage", message);
        }

        return ResultModel.Ok(message);
    }

    public async Task<ChatMessageModel[]> GetHistory(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId))
        {
            return [];
        }

        var values = await _redisDb.ListRangeAsync(GetChatKey(gameId));

        return values
            .Select(v => JsonSerializer.Deserialize<ChatMessageModel>(v.ToString()))
            .Where(m => m != null)
            .Select(m => m!)
            .ToArray();
    }

    private static string GetChatKey(string gameId) => $"{GAME_CONFIG}{gameId}{CHAT_CONFIG}";
    private static string GetCooldownKey(string userId) => $"{COOLDOWN_CONFIG}{userId}";
}
