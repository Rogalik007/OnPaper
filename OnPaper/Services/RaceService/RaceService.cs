using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;
using OnPaper.Hubs;
using OnPaper.Services.RaceRoomService;
using OnPaper.Services.RatingService;

namespace OnPaper.Services.RaceService;

// Матчмейкинг гонки живёт в памяти одного инстанса (без Redis): очередь и таймер ожидания -
// эфемерное состояние, которое не обязано переживать перезапуск сервера. Готовая комната,
// в отличие от очереди, уже сохраняется в Redis через IRaceRoomService, чтобы GetRaceState
// работал и после перезагрузки страницы.
public class RaceService(
    IHubContext<GameHub> hubContext, IRaceRoomService raceRoomService, IServiceScopeFactory scopeFactory)
    : IRaceService
{
    private const int MAZE_SIZE_X = 10;
    private const int MAZE_SIZE_Y = 10;

    // Абсолютный минимум, без которого гонки не будет вообще. Все 10 секунд ожидания
    // сервер пытается набрать 5 игроков, но по истечении таймера стартует и с меньшим
    // числом - лишь бы их было хотя бы MIN_PLAYERS.
    private const int MIN_PLAYERS = 2;

    private const int MAX_PLAYERS = 10;
    private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(10);

    private const int BULLETS = 3;
    private const int TURN_SECONDS = 20;

    // Ближе этого (в шагах по проходам) к кладу никто не стартует, иначе гонка закончится за пару ходов
    private const int MIN_TREASURE_DISTANCE = 5;

    // Ходы, выстрелы, таймауты и выход меняют одно и то же состояние гонки "прочитал - изменил - записал",
    // поэтому идут строго по одному. Сервис - синглтон, так что блокировка общая на все гонки.
    private readonly SemaphoreSlim _actionLock = new(1, 1);

    private readonly object _lock = new();
    private readonly List<WaitingPlayer> _pool = [];
    private CancellationTokenSource? _timerCts;

    public Task Search(string? userId, string? connectionId, string? nickname)
    {
        if (userId == null || connectionId == null || nickname == null)
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

            _pool.Add(new WaitingPlayer(userId, connectionId, nickname));

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
        var random = Random.Shared;
        var maze = RaceMazeGenerator.Generate(MAZE_SIZE_X, MAZE_SIZE_Y, random);
        maze.Treasure = random.Next(maze.CellsCount);

        var positions = RandomStartPositions(maze, players.Count, random);
        var gameId = Guid.NewGuid().ToString();

        var room = new RaceRoomModel
        {
            GameId = gameId,
            SizeX = MAZE_SIZE_X,
            SizeY = MAZE_SIZE_Y,
            Maze = maze,
            Players = players
                .Select((p, i) => new RacePlayerModel(p.UserId, p.ConnectionId, positions[i], p.Nickname))
                .ToArray()
        };

        // Кто ходит первым и в каком порядке дальше - случайно
        var order = players.Select(p => p.UserId).OrderBy(_ => random.Next()).ToList();

        var state = new RaceStateModel
        {
            TurnOrder = order,
            Turn = order[0],
            TurnDeadline = GetTurnDeadline(),
            Positions = room.Players.ToDictionary(p => p.UserId, p => p.Position),
            Bullets = room.Players.ToDictionary(p => p.UserId, _ => BULLETS),
            Visited = room.Players.ToDictionary(p => p.UserId, p => new List<int> { p.Position })
        };

        await raceRoomService.SaveRoom(room);
        await raceRoomService.SaveState(gameId, state);

        foreach (var player in players)
        {
            await hubContext.Clients.User(player.UserId).SendAsync("RaceFound", gameId);
        }
    }

    // Разные стартовые клетки подальше от клада. Если таких клеток не хватает - любые, кроме самого клада
    private static int[] RandomStartPositions(MazeModel maze, int count, Random random)
    {
        var distances = RaceMazeGenerator.Distances(maze, maze.Treasure!.Value);
        var cells = Enumerable.Range(0, maze.CellsCount).Where(c => distances[c] >= MIN_TREASURE_DISTANCE).ToList();

        if (cells.Count < count)
        {
            cells = Enumerable.Range(0, maze.CellsCount).Where(c => c != maze.Treasure).ToList();
        }

        return cells.OrderBy(_ => random.Next()).Take(count).ToArray();
    }

    public async Task<ResultModel<RaceSnapshotModel>> GetRaceState(string? userId, string? gameId,
        string? connectionId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<RaceSnapshotModel>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await raceRoomService.GetRoom(gameId);
        var player = room?.GetPlayer(userId);
        var state = room == null ? null : await raceRoomService.GetState(room.GameId);

        if (room == null || player == null || state == null)
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

        return ResultModel.Ok(new RaceSnapshotModel
        {
            Bullets = state.Bullets.GetValueOrDefault(userId),
            Alive = state.IsAlive(userId),
            Turn = state.Turn,
            SecondsLeft = GetSecondsLeft(state.TurnDeadline),
            Players = state.TurnOrder.Select(id => ToPlayerView(room, state, id)).ToList(),
            Winner = state.Winner,
            WinReason = state.WinReason
        });
    }

    // Карта игрока в его собственных координатах: старт - (0, 0). Настоящие координаты
    // и размер лабиринта наружу не уходят, поэтому по карте нельзя понять, где игрок на самом деле.
    private static RacePlayerViewModel ToPlayerView(RaceRoomModel room, RaceStateModel state, string userId)
    {
        var player = room.GetPlayer(userId)!;
        state.Eliminated.TryGetValue(userId, out var elimination);
        var position = ToRelative(room, player, state.Positions.GetValueOrDefault(userId, player.Position));

        return new RacePlayerViewModel
        {
            UserId = userId,
            Nickname = player.Nickname,
            Alive = elimination == null,
            EliminatedBy = elimination?.ByNickname,
            Left = elimination != null && elimination.ByUserId == null,
            X = position.X,
            Y = position.Y,
            Walls = state.GetDiscoveredWalls(userId).Select(w => ToRelativeWall(room, player, w)).ToList(),
            Visited = state.Visited.GetValueOrDefault(userId, []).Select(c => ToRelative(room, player, c)).ToList(),
            // Где лежал клад, показываем только после конца гонки
            Treasure = state.Winner != null ? ToRelative(room, player, room.Maze.Treasure!.Value) : null
        };
    }

    private static RaceCellModel ToRelative(RaceRoomModel room, RacePlayerModel player, int cell)
        => new(cell % room.SizeX - player.Position % room.SizeX, cell / room.SizeX - player.Position / room.SizeX);

    private static RaceWallModel ToRelativeWall(RaceRoomModel room, RacePlayerModel player, WallHitModel wall)
    {
        var cell = ToRelative(room, player, wall.Cell);
        return new RaceWallModel(cell.X, cell.Y, wall.Direction);
    }

    public Task<ResultModel<bool>> Move(string? userId, string? gameId, int direction)
        => RunTurnAction(userId, gameId, direction, async (room, state, me) =>
        {
            var maze = room.Maze;
            var position = state.Positions[me];
            var moveDirection = (Direction)direction;
            var isBlocked = maze.HasWall(position, moveDirection);

            if (isBlocked)
            {
                // Стену игрок видит только когда в неё упёрся - так лабиринт и открывается
                state.AddDiscoveredWall(me, position, direction);
            }
            else
            {
                position = maze.GetNeighbour(position, moveDirection);
                state.Positions[me] = position;
                state.AddVisited(me, position);

                if (position == maze.Treasure)
                {
                    state.Winner = me;
                    state.WinReason = "treasure";
                }
            }

            await FinishTurn(room, state, me);

            // Ход видят все - но на карте ходившего, в его координатах, а не в настоящих
            var player = room.GetPlayer(me)!;
            var cell = ToRelative(room, player, position);
            var wall = isBlocked ? new RaceWallModel(cell.X, cell.Y, direction) : null;

            await SendToAll(room, "RaceMoved", me, cell.X, cell.Y, wall);
            await AfterTurn(room, state);
        });

    // Пуля летит по прямой, пока не упрётся в стену, и попадает в первую клетку, где кто-то стоит
    // (если там несколько игроков - во всех). Свою клетку пуля не задевает.
    public Task<ResultModel<bool>> Shoot(string? userId, string? gameId, int direction)
        => RunTurnAction(userId, gameId, direction, async (room, state, me) =>
        {
            if (state.Bullets.GetValueOrDefault(me) <= 0)
            {
                throw new RaceActionException("No bullets left");
            }

            state.Bullets[me]--;

            var maze = room.Maze;
            var shotDirection = (Direction)direction;
            var cell = state.Positions[me];
            List<string> victims = [];

            while (victims.Count == 0 && !maze.HasWall(cell, shotDirection))
            {
                cell = maze.GetNeighbour(cell, shotDirection);
                victims = state.AlivePlayers().Where(id => id != me && state.Positions[id] == cell).ToList();
            }

            var shooter = room.GetPlayer(me)!;

            foreach (var victim in victims)
            {
                state.Eliminated[victim] = new RaceEliminationModel(me, shooter.Nickname);
            }

            CheckLastSurvivor(state);
            await FinishTurn(room, state, me);

            await hubContext.Clients.User(me).SendAsync("RaceShotResult", victims.Count > 0, state.Bullets[me]);

            // О выбывании сообщается всем участникам, включая самого выбывшего
            foreach (var victim in victims)
            {
                await SendToAll(room, "PlayerEliminated", victim, room.GetPlayer(victim)?.Nickname, me,
                    shooter.Nickname);
            }

            await AfterTurn(room, state);
        });

    // Вызывается клиентом, когда у ходящего истекли 20 секунд. Дедлайн проверяет сервер,
    // поэтому раньше времени ход не отобрать, а вызвать это может любой участник гонки.
    public async Task<ResultModel<bool>> TurnTimeout(string? userId, string? gameId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        await _actionLock.WaitAsync();

        try
        {
            var room = await raceRoomService.GetRoom(gameId);
            var state = room == null ? null : await raceRoomService.GetState(room.GameId);

            if (room?.GetPlayer(userId) == null || state == null)
            {
                return ResultModel.Fail<bool>("Race not found", ErrorType.NotFound);
            }

            if (state.Winner != null || !state.IsTurnExpired)
            {
                return ResultModel.Ok(false);
            }

            await FinishTurn(room, state, state.Turn);
            await AfterTurn(room, state);

            return ResultModel.Ok(true);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    // Выход в меню - игрок выбывает сразу, об этом узнают все
    public async Task<ResultModel<bool>> Leave(string? userId, string? gameId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        await _actionLock.WaitAsync();

        try
        {
            var room = await raceRoomService.GetRoom(gameId);
            var state = room == null ? null : await raceRoomService.GetState(room.GameId);
            var player = room?.GetPlayer(userId);

            if (room == null || player == null || state == null)
            {
                return ResultModel.Fail<bool>("Race not found", ErrorType.NotFound);
            }

            if (state.Winner != null || !state.IsAlive(userId))
            {
                return ResultModel.Ok(false);
            }

            state.Eliminated[userId] = new RaceEliminationModel(null, null);
            CheckLastSurvivor(state);

            var wasTurn = state.Turn == userId;

            if (wasTurn)
            {
                await FinishTurn(room, state, userId);
            }
            else
            {
                await raceRoomService.SaveState(room.GameId, state);
            }

            await SendToAll(room, "PlayerEliminated", userId, player.Nickname, null, null);

            if (wasTurn || state.Winner != null)
            {
                await AfterTurn(room, state);
            }

            return ResultModel.Ok(true);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    // Общая обвязка хода и выстрела: проверки очереди и дедлайна под блокировкой
    private async Task<ResultModel<bool>> RunTurnAction(string? userId, string? gameId, int direction,
        Func<RaceRoomModel, RaceStateModel, string, Task> action)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        if (!Enum.IsDefined(typeof(Direction), direction))
        {
            return ResultModel.Fail<bool>("Unknown direction", ErrorType.BadRequest);
        }

        await _actionLock.WaitAsync();

        try
        {
            var room = await raceRoomService.GetRoom(gameId);
            var state = room == null ? null : await raceRoomService.GetState(room.GameId);

            if (room?.GetPlayer(userId) == null || state == null)
            {
                return ResultModel.Fail<bool>("You are not in this race", ErrorType.BadRequest);
            }

            if (state.Winner != null)
            {
                return ResultModel.Fail<bool>("Race is over", ErrorType.BadRequest);
            }

            if (!state.IsAlive(userId))
            {
                return ResultModel.Fail<bool>("You are out of the race", ErrorType.BadRequest);
            }

            if (state.Turn != userId)
            {
                return ResultModel.Fail<bool>("It is not your turn", ErrorType.BadRequest);
            }

            // Ход после дедлайна не считается - вместо него очередь просто переходит дальше
            if (state.IsTurnExpired)
            {
                await FinishTurn(room, state, userId);
                await AfterTurn(room, state);
                return ResultModel.Fail<bool>("Time is up", ErrorType.BadRequest);
            }

            await action(room, state, userId);

            return ResultModel.Ok(true);
        }
        catch (RaceActionException e)
        {
            return ResultModel.Fail<bool>(e.Message, ErrorType.BadRequest);
        }
        finally
        {
            _actionLock.Release();
        }
    }

    private static void CheckLastSurvivor(RaceStateModel state)
    {
        var alive = state.AlivePlayers();

        if (state.Winner == null && alive.Count == 1)
        {
            state.Winner = alive[0];
            state.WinReason = "survivor";
        }
    }

    // Передаёт очередь следующему живому игроку (если гонка не кончилась) и сохраняет состояние
    private async Task FinishTurn(RaceRoomModel room, RaceStateModel state, string from)
    {
        if (state.Winner == null)
        {
            state.Turn = state.NextTurn(from);
            state.TurnDeadline = GetTurnDeadline();
        }

        await raceRoomService.SaveState(room.GameId, state);
    }

    // Рассылает новую очередь или итог гонки
    private async Task AfterTurn(RaceRoomModel room, RaceStateModel state)
    {
        if (state.Winner == null)
        {
            await SendToAll(room, "RaceTurn", state.Turn, TURN_SECONDS);
            return;
        }

        if (int.TryParse(state.Winner, out var winnerId))
        {
            // RatingService работает с DbContext и потому scoped, а этот сервис - синглтон
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IRatingService>().AddWinPoints(winnerId);
        }

        await SendToAll(room, "RaceOver", state.Winner, room.GetPlayer(state.Winner)?.Nickname, state.WinReason,
            room.Maze.Treasure);
    }

    // Адресуем по UserId: после перезагрузки страницы соединение новое, а пользователь тот же
    private Task SendToAll(RaceRoomModel room, string method, params object?[] args)
        => hubContext.Clients.Users(room.Players.Select(p => p.UserId).ToList()).SendCoreAsync(method, args);

    private static DateTimeOffset GetTurnDeadline() => DateTimeOffset.UtcNow.AddSeconds(TURN_SECONDS);

    private static int GetSecondsLeft(DateTimeOffset deadline)
    {
        var left = (deadline - DateTimeOffset.UtcNow).TotalSeconds;

        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    private class RaceActionException(string message) : Exception(message);

    private record WaitingPlayer(string UserId, string ConnectionId, string Nickname);
}
