using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;
using OnPaper.Hubs;
using OnPaper.Services.RaceRoomService;

namespace OnPaper.Services.RaceService;

// Матчмейкинг гонки живёт в памяти одного инстанса (без Redis): очередь и таймер ожидания -
// эфемерное состояние, которое не обязано переживать перезапуск сервера. Готовая комната,
// в отличие от очереди, уже сохраняется в Redis через IRaceRoomService, чтобы GetRaceState
// работал и после перезагрузки страницы.
public class RaceService(IHubContext<GameHub> hubContext, IRaceRoomService raceRoomService) : IRaceService
{
    private const int MAZE_SIZE_X = 10;
    private const int MAZE_SIZE_Y = 10;

    // Абсолютный минимум, без которого гонки не будет вообще. Все 10 секунд ожидания
    // сервер пытается набрать 5 игроков, но по истечении таймера стартует и с меньшим
    // числом - лишь бы их было хотя бы MIN_PLAYERS.
    private const int MIN_PLAYERS = 2;

    private const int MAX_PLAYERS = 10;
    private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(10);

    private static readonly Random Random = new();

    private readonly object _lock = new();
    private readonly List<WaitingPlayer> _pool = [];
    private CancellationTokenSource? _timerCts;

    public Task Search(string? userId, string? connectionId)
    {
        if (userId == null || connectionId == null)
        {
            return Task.CompletedTask;
        }

        List<WaitingPlayer>? batch = null;

        lock (_lock)
        {
            if (_pool.Any(p => p.UserId == userId))
            {
                throw new HubException("You are already in queue");
            }

            _pool.Add(new WaitingPlayer(userId, connectionId));

            if (_pool.Count >= MAX_PLAYERS)
            {
                // Набрали потолок в 10 игроков - ждать оставшиеся секунды таймера смысла нет
                _timerCts?.Cancel();
                batch = _pool.Take(MAX_PLAYERS).ToList();
                _pool.RemoveRange(0, MAX_PLAYERS);
                _timerCts = _pool.Count > 0 ? StartTimer() : null;
            }
            else
            {
                // Таймер запускает только первый игрок очереди - остальные просто в неё попадают
                _timerCts ??= StartTimer();
            }
        }

        return batch != null ? StartRace(batch) : Task.CompletedTask;
    }

    public Task CancelSearch(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.CompletedTask;
        }

        lock (_lock)
        {
            _pool.RemoveAll(p => p.UserId == userId);

            // Очередь опустела - таймер больше нечего ждать, следующий игрок запустит новый
            if (_pool.Count == 0 && _timerCts != null)
            {
                _timerCts.Cancel();
                _timerCts = null;
            }
        }

        return Task.CompletedTask;
    }

    private CancellationTokenSource StartTimer()
    {
        var cts = new CancellationTokenSource();
        _ = RunTimer(cts.Token);
        return cts;
    }

    private async Task RunTimer(CancellationToken token)
    {
        try
        {
            await Task.Delay(WaitTime, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        List<WaitingPlayer> batch;

        lock (_lock)
        {
            // Пока ждали лока, Search уже мог сам разобрать очередь (набрался потолок в 10
            // игроков) и отменить этот таймер - тогда обрабатывать пул повторно не нужно.
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Меньше двух игроков ждать 5 бессмысленно ещё 10 секунд - просто гасим таймер,
            // следующий зашедший в очередь игрок запустит его заново.
            if (_pool.Count < MIN_PLAYERS)
            {
                _timerCts = null;
                return;
            }

            var takeCount = Math.Min(_pool.Count, MAX_PLAYERS);
            batch = _pool.Take(takeCount).ToList();
            _pool.RemoveRange(0, takeCount);

            // Если пришло больше игроков, чем влезло в эту гонку - для оставшихся запускаем
            // отдельное десятисекундное ожидание, как для новой очереди
            _timerCts = _pool.Count > 0 ? StartTimer() : null;
        }

        await StartRace(batch);
    }

    private async Task StartRace(List<WaitingPlayer> players)
    {
        var positions = RandomDistinctPositions(players.Count);
        var gameId = Guid.NewGuid().ToString();

        var room = new RaceRoomModel
        {
            GameId = gameId,
            SizeX = MAZE_SIZE_X,
            SizeY = MAZE_SIZE_Y,
            Players = players.Select((p, i) => new RacePlayerModel(p.UserId, p.ConnectionId, positions[i])).ToArray()
        };

        await raceRoomService.SaveRoom(room);
        await raceRoomService.InitPositions(gameId, room.Players.ToDictionary(p => p.UserId, p => p.Position));

        foreach (var player in players)
        {
            await hubContext.Clients.User(player.UserId).SendAsync("RaceFound", gameId);
        }
    }

    private static int[] RandomDistinctPositions(int count)
    {
        var cells = Enumerable.Range(0, MAZE_SIZE_X * MAZE_SIZE_Y).ToList();
        var result = new int[count];

        for (var i = 0; i < count; i++)
        {
            var index = Random.Next(cells.Count);
            result[i] = cells[index];
            cells.RemoveAt(index);
        }

        return result;
    }

    public async Task<ResultModel<RaceSnapshotModel>> GetRaceState(string? userId, string? gameId,
        string? connectionId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<RaceSnapshotModel>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await raceRoomService.GetRoom(gameId);

        if (room == null)
        {
            return ResultModel.Fail<RaceSnapshotModel>("Race not found", ErrorType.NotFound);
        }

        var player = room.Players.FirstOrDefault(p => p.UserId == userId);

        if (player == null)
        {
            return ResultModel.Fail<RaceSnapshotModel>("Race not found", ErrorType.NotFound);
        }

        // После перезагрузки страницы соединение новое - обновляем его в комнате
        if (!string.IsNullOrEmpty(connectionId) && player.ConnectionId != connectionId)
        {
            room.Players = room.Players
                .Select(p => p.UserId == userId ? p with { ConnectionId = connectionId } : p)
                .ToArray();

            await raceRoomService.SaveRoom(room);
        }

        var positions = await raceRoomService.GetPositions(room.GameId);

        int GetPosition(string uid) =>
            positions.TryGetValue(uid, out var p) ? p : room.Players.First(pl => pl.UserId == uid).Position;

        return ResultModel.Ok(new RaceSnapshotModel
        {
            SizeX = room.SizeX,
            SizeY = room.SizeY,
            MyPosition = GetPosition(userId),
            Players = room.Players.Select(p => new RacePlayerPositionModel(p.UserId, GetPosition(p.UserId))).ToList()
        });
    }

    // Стенок пока нет, поэтому упереться можно только в край поля - направление,
    // ведущее за границу, просто не двигает игрока.
    public async Task<ResultModel<bool>> Move(string? userId, string? gameId, int direction, IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        if (!Enum.IsDefined(typeof(Direction), direction))
        {
            return ResultModel.Fail<bool>("Unknown direction", ErrorType.BadRequest);
        }

        var room = await raceRoomService.GetRoom(gameId);

        if (room == null || room.Players.All(p => p.UserId != userId))
        {
            return ResultModel.Fail<bool>("You are not in this race", ErrorType.BadRequest);
        }

        var position = await raceRoomService.GetPosition(room.GameId, userId);

        if (position == null)
        {
            return ResultModel.Fail<bool>("You are not in this race", ErrorType.BadRequest);
        }

        var newPosition = ApplyMove(position.Value, room.SizeX, room.SizeY, (Direction)direction);

        if (newPosition != position.Value)
        {
            await raceRoomService.SetPosition(room.GameId, userId, newPosition);
        }

        foreach (var player in room.Players)
        {
            await clients.User(player.UserId).SendAsync("PlayerMoved", userId, newPosition);
        }

        return ResultModel.Ok(true);
    }

    private static int ApplyMove(int position, int sizeX, int sizeY, Direction direction)
    {
        var x = position % sizeX;
        var y = position / sizeX;

        switch (direction)
        {
            case Direction.Up:
                y = Math.Max(0, y - 1);
                break;
            case Direction.Down:
                y = Math.Min(sizeY - 1, y + 1);
                break;
            case Direction.Left:
                x = Math.Max(0, x - 1);
                break;
            case Direction.Right:
                x = Math.Min(sizeX - 1, x + 1);
                break;
        }

        return y * sizeX + x;
    }

    private record WaitingPlayer(string UserId, string ConnectionId);
}
