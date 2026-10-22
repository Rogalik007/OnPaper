using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;
using OnPaper.Services.ChatService;
using OnPaper.Services.GameRoomService;
using OnPaper.Services.RatingService;
using StackExchange.Redis;

namespace OnPaper.Services.GameTreasureService;

public class GameTreasureService(
    IConnectionMultiplexer redis, IGameRoomService gameRoomService, IRatingService ratingService,
    IChatService chatService)
    : IGameTreasureService
{
    private readonly IDatabase _redisDb = redis.GetDatabase();

    private const int MAZE_SIZE_X = 5;
    private const int MAZE_SIZE_Y = 6;

    // Ровно столько мин и супер-мин обязаны быть на каждом лабиринте.
    private const int REQUIRED_MINES_COUNT = 2;

    // Оба игрока начинают из левого верхнего угла чужого лабиринта.
    private const int START_CELL = 0;

    // Сколько секунд даётся на ход
    private const int TURN_SECONDS = 20;

    // Сколько ждём отвалившегося игрока, прежде чем засчитать поражение
    private const int LEAVE_SECONDS = 20;

    private const string MAZE_CONFIG = "maze:";
    private static readonly TimeSpan MazeLifetime = TimeSpan.FromHours(2);

    private static readonly Direction[] AllDirections =
        [Direction.Up, Direction.Right, Direction.Down, Direction.Left];

    public int[] GetMazeSize() => [MAZE_SIZE_X, MAZE_SIZE_Y];

    // Состояние партии для клиента: и при первом заходе, и после перезагрузки страницы.
    // Здесь же отсекаются чужие и несуществующие игры.
    public async Task<ResultModel<GameSnapshotModel>> GetGameState(string? userId, string? gameId,
        string? connectionId, IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<GameSnapshotModel>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);

        if (room == null)
        {
            return ResultModel.Fail<GameSnapshotModel>("Game not found", ErrorType.NotFound);
        }

        var player = room.GetPlayer(userId);
        var opponent = room.GetOpponent(userId);

        if (player == null || opponent == null)
        {
            return ResultModel.Fail<GameSnapshotModel>("Game not found", ErrorType.NotFound);
        }

        // После перезагрузки страницы соединение новое - обновляем его в комнате
        if (!string.IsNullOrEmpty(connectionId) && player.ConnectionId != connectionId)
        {
            room.Players = room.Players
                .Select(p => p.UserId == userId ? p with { ConnectionId = connectionId } : p)
                .ToArray();

            await gameRoomService.SaveRoom(room);
        }

        await gameRoomService.SetPlayerGame(userId, room.GameId);

        var snapshot = new GameSnapshotModel
        {
            Size = GetMazeSize(),
            MyMaze = await GetMaze(room.GameId, userId),
            ChatMessages = await chatService.GetHistory(room.GameId),
            OpponentNickname = opponent.Nickname
        };

        var left = await gameRoomService.GetPlayerLeft(room.GameId);

        if (left != null && left.UserId == userId)
        {
            // Игрок вернулся (перезагрузил страницу) - снимаем отсчёт у противника
            await gameRoomService.ClearPlayerLeft(room.GameId);
            await clients.User(opponent.UserId).SendAsync("OpponentReturned");
        }
        else if (left != null)
        {
            snapshot.OpponentLeftSeconds = GetSecondsLeft(left.Deadline);
        }

        var state = await gameRoomService.GetState(room.GameId);

        if (state == null)
        {
            return ResultModel.Ok(snapshot);
        }

        snapshot.IsStarted = true;
        snapshot.MyPosition = state.GetPosition(userId);
        snapshot.OpponentPosition = state.GetPosition(opponent.UserId);
        snapshot.YourTurn = state.Turn == userId;
        snapshot.SecondsLeft = GetSecondsLeft(state.TurnDeadline);
        snapshot.DiscoveredWalls = [.. state.GetDiscoveredWalls(userId)];
        snapshot.DiscoveredMines = [.. state.GetDiscoveredMines(userId)];
        snapshot.YouWon = state.Winner == null ? null : state.Winner == userId;
        snapshot.EndedByDisconnect = state.EndedByDisconnect;

        return ResultModel.Ok(snapshot);
    }

    // Соединение оборвалось: предупреждаем оставшегося игрока и даём ушедшему время вернуться.
    // Перезагрузка страницы тоже сюда попадает, поэтому партия заканчивается не сразу.
    public async Task PlayerLeft(string? userId, string? connectionId, IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        var gameId = await gameRoomService.GetPlayerGame(userId);
        var room = await gameRoomService.GetRoom(gameId);
        var opponent = room?.GetOpponent(userId);

        if (room == null || opponent == null)
        {
            return;
        }

        // Закрылось не то соединение, через которое игрок сейчас в партии: старая вкладка
        // или соединение, обрыв которого сервер заметил уже после переподключения клиента.
        // Игрок на месте, засчитывать уход нельзя.
        if (room.GetPlayer(userId)?.ConnectionId != connectionId)
        {
            return;
        }

        var state = await gameRoomService.GetState(room.GameId);

        if (state?.Winner != null)
        {
            return;
        }

        await gameRoomService.SetPlayerLeft(room.GameId,
            new PlayerLeftModel(userId, DateTimeOffset.UtcNow.AddSeconds(LEAVE_SECONDS)));

        await clients.User(opponent.UserId).SendAsync("OpponentLeft", LEAVE_SECONDS);
    }

    // Вызывается оставшимся игроком, когда время ожидания вышло. Дедлайн проверяет сервер.
    public async Task<ResultModel<bool>> CheckOpponentLeft(string? userId, string? gameId,
        IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);
        var opponent = room?.GetOpponent(userId);

        if (room == null || room.GetPlayer(userId) == null || opponent == null)
        {
            return ResultModel.Fail<bool>("Game not found", ErrorType.NotFound);
        }

        var left = await gameRoomService.GetPlayerLeft(room.GameId);

        // Игрок мог вернуться или уйти уже после проверки - это не ошибка
        if (left == null || left.UserId != opponent.UserId || DateTimeOffset.UtcNow < left.Deadline)
        {
            return ResultModel.Ok(false);
        }

        var state = await gameRoomService.GetState(room.GameId) ?? new GameStateModel();

        if (state.Winner != null)
        {
            return ResultModel.Ok(false);
        }

        state.Winner = userId;
        state.EndedByDisconnect = true;

        await gameRoomService.SaveState(room.GameId, state);
        await gameRoomService.ClearPlayerLeft(room.GameId);
        await EndGame(room, userId, clients, byDisconnect: true);

        return ResultModel.Ok(true);
    }

    // Явный выход игрока в меню: в отличие от обрыва соединения, здесь не даём
    // время на возврат - игрок сам решил уйти, поэтому партия заканчивается сразу.
    public async Task<ResultModel<bool>> LeaveGame(string? userId, string? gameId, IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);
        var opponent = room?.GetOpponent(userId);

        if (room == null || room.GetPlayer(userId) == null || opponent == null)
        {
            return ResultModel.Fail<bool>("Game not found", ErrorType.NotFound);
        }

        var state = await gameRoomService.GetState(room.GameId) ?? new GameStateModel();

        if (state.Winner != null)
        {
            return ResultModel.Ok(false);
        }

        state.Winner = opponent.UserId;
        state.EndedByDisconnect = true;

        await gameRoomService.SaveState(room.GameId, state);
        await gameRoomService.ClearPlayerLeft(room.GameId);
        await EndGame(room, opponent.UserId, clients, byDisconnect: true);

        return ResultModel.Ok(true);
    }

    private static int GetSecondsLeft(DateTimeOffset deadline)
    {
        var left = (deadline - DateTimeOffset.UtcNow).TotalSeconds;

        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    public async Task<ResultModel<bool>> SaveMaze(string? userId, string? gameId, MazeModel? maze,
        IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);

        if (room == null)
        {
            return ResultModel.Fail<bool>("Game not found", ErrorType.BadRequest);
        }

        var opponent = room.GetOpponent(userId);

        if (room.GetPlayer(userId) == null || opponent == null)
        {
            return ResultModel.Fail<bool>("You are not in this game", ErrorType.BadRequest);
        }

        // Партия могла закончиться, пока игрок рисовал: например, он сам отвалился и не успел вернуться
        var currentState = await gameRoomService.GetState(room.GameId);

        if (currentState?.Winner != null)
        {
            return ResultModel.Fail<bool>("Game is over", ErrorType.BadRequest);
        }

        var validation = Validate(maze);

        if (!validation.IsSuccess)
        {
            return validation;
        }

        string json = JsonSerializer.Serialize(maze);

        bool isSaved;
        try
        {
            // Лабиринт сохраняется один раз: When.NotExists не даст перезаписать уже отправленный,
            // иначе противник будет ходить по постоянно меняющемуся полю.
            isSaved = await _redisDb.StringSetAsync(
                GetMazeKey(room.GameId, userId), json, MazeLifetime, when: When.NotExists);
        }
        catch (Exception)
        {
            return ResultModel.Fail<bool>("Internal server error", ErrorType.ServerError);
        }

        if (!isSaved)
        {
            return ResultModel.Fail<bool>("Maze is already saved", ErrorType.Conflict);
        }

        // Партия начинается, когда лабиринты нарисовали оба
        if (await _redisDb.KeyExistsAsync(GetMazeKey(room.GameId, opponent.UserId)))
        {
            await TryStartGame(room, clients);
        }

        return ResultModel.Ok(true);
    }

    public async Task<ResultModel<bool>> Move(string? userId, string? gameId, int direction,
        IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        if (!Enum.IsDefined(typeof(Direction), direction))
        {
            return ResultModel.Fail<bool>("Unknown direction", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);

        if (room == null)
        {
            return ResultModel.Fail<bool>("Game not found", ErrorType.BadRequest);
        }

        var opponent = room.GetOpponent(userId);

        if (room.GetPlayer(userId) == null || opponent == null)
        {
            return ResultModel.Fail<bool>("You are not in this game", ErrorType.BadRequest);
        }

        var state = await gameRoomService.GetState(room.GameId);

        if (state == null)
        {
            return ResultModel.Fail<bool>("Game is not started yet", ErrorType.BadRequest);
        }

        if (state.Winner != null)
        {
            return ResultModel.Fail<bool>("Game is over", ErrorType.BadRequest);
        }

        if (state.Turn != userId)
        {
            return ResultModel.Fail<bool>("It is not your turn", ErrorType.BadRequest);
        }

        // Ход, пришедший после дедлайна, не считается - вместо него ход просто переходит противнику
        if (state.IsTurnExpired)
        {
            await PassTurn(room, userId, opponent.UserId, state, clients);
            return ResultModel.Fail<bool>("Time is up", ErrorType.BadRequest);
        }

        // Игрок ходит по лабиринту противника
        var maze = await GetMaze(room.GameId, opponent.UserId);

        if (maze == null)
        {
            return ResultModel.Fail<bool>("Internal server error", ErrorType.ServerError);
        }

        var position = state.GetPosition(userId);
        var moveDirection = (Direction)direction;
        var isBlocked = maze.HasWall(position, moveDirection);
        string? minePenalty = null;

        if (isBlocked)
        {
            state.AddDiscoveredWall(userId, position, direction);
        }
        else
        {
            position = maze.GetNeighbour(position, moveDirection);
            state.Positions[userId] = position;

            if (position == maze.Treasure)
            {
                state.Winner = userId;
            }
            else if (position == maze.SuperMine)
            {
                minePenalty = "supermine";
                state.AddDiscoveredMine(userId, position, isSuper: true);
                state.AddSkipTurns(userId, 2);
            }
            else if (maze.Mines.Contains(position))
            {
                minePenalty = "mine";
                state.AddDiscoveredMine(userId, position, isSuper: false);
                state.AddSkipTurns(userId, 1);
            }
        }

        // Ход переходит к противнику в любом случае: упереться в стенку - тоже ход.
        // Если у противника накоплены пропуски ходов от мин, GetNextTurn может вернуть ход обратно.
        if (state.Winner == null)
        {
            state.Turn = state.GetNextTurn(userId, opponent.UserId);
            state.TurnDeadline = GetTurnDeadline();
        }

        await gameRoomService.SaveState(room.GameId, state);

        // Стенку игрок видит только когда в неё упёрся - так поле и открывается
        await clients.User(userId).SendAsync("MoveResult", position, isBlocked ? direction : null, minePenalty);

        // А противник видит, как по его собственному лабиринту ходят
        await clients.User(opponent.UserId).SendAsync("OpponentMoved", position);

        if (state.Winner != null)
        {
            await EndGame(room, state.Winner, clients);
        }
        else
        {
            await SendTurn(room, state.Turn, clients);
        }

        return ResultModel.Ok(true);
    }

    // Вызывается клиентом, когда у игрока истекли 20 секунд. Сервер сам сверяется с дедлайном,
    // поэтому раньше времени ход не отобрать, а вызвать это может любой из двоих.
    public async Task<ResultModel<bool>> TurnTimeout(string? userId, string? gameId, IHubCallerClients clients)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return ResultModel.Fail<bool>("User is not authorized", ErrorType.BadRequest);
        }

        var room = await gameRoomService.GetRoom(gameId);

        if (room == null)
        {
            return ResultModel.Fail<bool>("Game not found", ErrorType.BadRequest);
        }

        if (room.GetPlayer(userId) == null)
        {
            return ResultModel.Fail<bool>("You are not in this game", ErrorType.BadRequest);
        }

        var state = await gameRoomService.GetState(room.GameId);

        // Ход уже мог перейти сам, поэтому это не ошибка - просто ничего не делаем
        if (state == null || state.Winner != null || !state.IsTurnExpired)
        {
            return ResultModel.Ok(false);
        }

        var next = room.GetOpponent(state.Turn);

        if (next == null)
        {
            return ResultModel.Ok(false);
        }

        await PassTurn(room, state.Turn, next.UserId, state, clients);

        return ResultModel.Ok(true);
    }

    // Просроченный ход сгорает, но очередь дальше идёт как после обычного хода: если противник
    // должен пропустить ходы из-за мин, они списываются и здесь, а не откладываются на потом.
    private async Task PassTurn(GameRoomModel room, string from, string to, GameStateModel state,
        IHubCallerClients clients)
    {
        state.Turn = state.GetNextTurn(from, to);
        state.TurnDeadline = GetTurnDeadline();

        await gameRoomService.SaveState(room.GameId, state);
        await SendTurn(room, state.Turn, clients);
    }

    private static DateTimeOffset GetTurnDeadline() => DateTimeOffset.UtcNow.AddSeconds(TURN_SECONDS);

    public async Task<MazeModel?> GetMaze(string? gameId, string? userId)
    {
        if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var json = await _redisDb.StringGetAsync(GetMazeKey(gameId, userId));

        if (json.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MazeModel>(json.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Лабиринт принадлежит партии, а не игроку: иначе вторая партия того же игрока
    // упёрлась бы в уже сохранённый лабиринт из первой.
    private static string GetMazeKey(string gameId, string userId) => $"{MAZE_CONFIG}{gameId}:{userId}";

    // Каждый игрок ходит по лабиринту противника, поэтому видит только свою позицию:
    // само поле для него пустое, стенки открываются по ходу игры.
    private async Task TryStartGame(GameRoomModel room, IHubCallerClients clients)
    {
        var state = new GameStateModel
        {
            Turn = room.Players[0].UserId,
            Positions = room.Players.ToDictionary(p => p.UserId, _ => START_CELL),
            TurnDeadline = GetTurnDeadline()
        };

        if (!await gameRoomService.TryStartGame(room.GameId, state))
        {
            return;
        }

        foreach (var player in room.Players)
        {
            await clients.User(player.UserId)
                .SendAsync("GameStart", GetMazeSize(), START_CELL, player.UserId == state.Turn, TURN_SECONDS);
        }
    }

    // Адресуем по UserId, а не по ConnectionId: после перезагрузки страницы или переподключения
    // соединение новое, а пользователь тот же.
    private static async Task SendTurn(GameRoomModel room, string turn, IHubCallerClients clients)
    {
        foreach (var player in room.Players)
        {
            await clients.User(player.UserId)
                .SendAsync("TurnChanged", player.UserId == turn, TURN_SECONDS);
        }
    }

    // Партия закончилась - начисляем очки победителю в рейтинге и оповещаем обоих игроков.
    private async Task EndGame(GameRoomModel room, string winner, IHubCallerClients clients,
        bool byDisconnect = false)
    {
        if (int.TryParse(winner, out var winnerId))
        {
            await ratingService.AddWinPoints(winnerId);
        }

        foreach (var player in room.Players)
        {
            await clients.User(player.UserId).SendAsync("GameOver", player.UserId == winner, byDisconnect);
        }
    }

    private static ResultModel<bool> Validate(MazeModel? maze)
    {
        if (maze == null)
        {
            return ResultModel.Fail<bool>("Maze is required", ErrorType.BadRequest);
        }

        if (maze.SizeX != MAZE_SIZE_X || maze.SizeY != MAZE_SIZE_Y)
        {
            return ResultModel.Fail<bool>($"Maze size must be {MAZE_SIZE_X}x{MAZE_SIZE_Y}", ErrorType.BadRequest);
        }

        if (maze.HorizontalWalls.Length != maze.HorizontalWallsCount)
        {
            return ResultModel.Fail<bool>(
                $"Horizontal walls count must be {maze.HorizontalWallsCount}", ErrorType.BadRequest);
        }

        if (maze.VerticalWalls.Length != maze.VerticalWallsCount)
        {
            return ResultModel.Fail<bool>(
                $"Vertical walls count must be {maze.VerticalWallsCount}", ErrorType.BadRequest);
        }

        if (maze.HorizontalWalls.Any(w => w is not (0 or 1)) || maze.VerticalWalls.Any(w => w is not (0 or 1)))
        {
            return ResultModel.Fail<bool>("Wall value must be 0 or 1", ErrorType.BadRequest);
        }

        if (maze.Treasure == null)
        {
            return ResultModel.Fail<bool>("Treasure is not placed", ErrorType.BadRequest);
        }

        if (maze.Treasure < 0 || maze.Treasure >= maze.CellsCount)
        {
            return ResultModel.Fail<bool>("Treasure is out of the maze", ErrorType.BadRequest);
        }

        if (maze.Mines.Length != REQUIRED_MINES_COUNT)
        {
            return ResultModel.Fail<bool>($"Exactly {REQUIRED_MINES_COUNT} mines must be placed", ErrorType.BadRequest);
        }

        if (maze.Mines.Any(m => m < 0 || m >= maze.CellsCount))
        {
            return ResultModel.Fail<bool>("Mine is out of the maze", ErrorType.BadRequest);
        }

        if (maze.Mines.Distinct().Count() != maze.Mines.Length)
        {
            return ResultModel.Fail<bool>("Mines must not overlap", ErrorType.BadRequest);
        }

        if (maze.SuperMine == null)
        {
            return ResultModel.Fail<bool>("Supermine is not placed", ErrorType.BadRequest);
        }

        if (maze.SuperMine < 0 || maze.SuperMine >= maze.CellsCount)
        {
            return ResultModel.Fail<bool>("Supermine is out of the maze", ErrorType.BadRequest);
        }

        // Игрок начинает на стартовой клетке, а не входит в неё: клад там нельзя найти, а мина не сработает
        if (maze.Treasure == START_CELL || maze.SuperMine == START_CELL || maze.Mines.Contains(START_CELL))
        {
            return ResultModel.Fail<bool>(
                "Treasure and mines must not be placed on the start cell", ErrorType.BadRequest);
        }

        if (maze.Mines.Contains(maze.SuperMine.Value))
        {
            return ResultModel.Fail<bool>("Supermine must not overlap a mine", ErrorType.BadRequest);
        }

        if (maze.Treasure == maze.SuperMine || maze.Mines.Contains(maze.Treasure.Value))
        {
            return ResultModel.Fail<bool>("Mines must not overlap the treasure", ErrorType.BadRequest);
        }

        if (!IsConnected(maze))
        {
            return ResultModel.Fail<bool>("Maze has unreachable cells", ErrorType.BadRequest);
        }

        return ResultModel.Ok(true);
    }

    // Обход в ширину от первой клетки: из любой клетки должна быть достижима любая другая,
    // иначе стенками можно замуровать клад или запереть противника в углу.
    private static bool IsConnected(MazeModel maze)
    {
        var visited = new bool[maze.CellsCount];
        var queue = new Queue<int>();

        visited[0] = true;
        queue.Enqueue(0);
        var visitedCount = 1;

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();

            foreach (var direction in AllDirections)
            {
                var next = maze.GetNeighbour(cell, direction);

                if (next < 0 || visited[next])
                {
                    continue;
                }

                visited[next] = true;
                visitedCount++;
                queue.Enqueue(next);
            }
        }

        return visitedCount == maze.CellsCount;
    }
}
