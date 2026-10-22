using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OnPaper.Common.Models.Utils;

// ConnectionId нужен, чтобы адресовать игрока в комнате; Position - стартовая клетка.
public record RacePlayerModel(string UserId, string ConnectionId, int Position, string Nickname);

// Неизменная часть гонки: состав игроков и сгенерированный лабиринт вместе с кладом.
// Клиенту лабиринт целиком никогда не уходит - стены открываются, только когда в них упрёшься.
public class RaceRoomModel
{
    public string GameId { get; set; } = string.Empty;
    public int SizeX { get; set; }
    public int SizeY { get; set; }
    public RacePlayerModel[] Players { get; set; } = [];
    public MazeModel Maze { get; set; } = new();

    public RacePlayerModel? GetPlayer(string userId) => Players.FirstOrDefault(p => p.UserId == userId);
}

// Изменяемая часть гонки: чья очередь, где кто стоит, сколько патронов, кто выбыл.
public class RaceStateModel
{
    // Порядок ходов, выбранный случайно при старте. Выбывшие из него не удаляются - их просто пропускают
    public List<string> TurnOrder { get; set; } = [];

    public string Turn { get; set; } = string.Empty;
    public DateTimeOffset TurnDeadline { get; set; }

    public Dictionary<string, int> Positions { get; set; } = new();
    public Dictionary<string, int> Bullets { get; set; } = new();

    // UserId выбывшего -> как выбыл (кто застрелил или что он сам вышел)
    public Dictionary<string, RaceEliminationModel> Eliminated { get; set; } = new();

    // Стены, в которые игрок уже упирался, - у каждого своя открытая часть лабиринта
    public Dictionary<string, List<WallHitModel>> DiscoveredWalls { get; set; } = new();

    // Клетки, где игрок уже побывал, - на его карте это пройденный путь
    public Dictionary<string, List<int>> Visited { get; set; } = new();

    public string? Winner { get; set; }

    // "treasure" - нашёл клад, "survivor" - остался последним живым
    public string? WinReason { get; set; }

    [JsonIgnore]
    public bool IsTurnExpired => DateTimeOffset.UtcNow > TurnDeadline;

    public bool IsAlive(string userId) => !Eliminated.ContainsKey(userId);

    public List<string> AlivePlayers() => TurnOrder.Where(IsAlive).ToList();

    // Следующий живой игрок после from по кругу. Если живых не осталось - сам from.
    public string NextTurn(string from)
    {
        var start = TurnOrder.IndexOf(from);

        for (var i = 1; i <= TurnOrder.Count; i++)
        {
            var candidate = TurnOrder[(start + i) % TurnOrder.Count];

            if (IsAlive(candidate))
            {
                return candidate;
            }
        }

        return from;
    }

    public void AddVisited(string userId, int cell)
    {
        if (!Visited.TryGetValue(userId, out var cells))
        {
            cells = [];
            Visited[userId] = cells;
        }

        if (!cells.Contains(cell))
        {
            cells.Add(cell);
        }
    }

    public List<WallHitModel> GetDiscoveredWalls(string userId)
        => DiscoveredWalls.TryGetValue(userId, out var walls) ? walls : [];

    public void AddDiscoveredWall(string userId, int cell, int direction)
    {
        if (!DiscoveredWalls.TryGetValue(userId, out var walls))
        {
            walls = [];
            DiscoveredWalls[userId] = walls;
        }

        if (!walls.Any(w => w.Cell == cell && w.Direction == direction))
        {
            walls.Add(new WallHitModel(cell, direction));
        }
    }
}

// ByUserId == null - игрок сам вышел из гонки
public record RaceEliminationModel(string? ByUserId, string? ByNickname);

// Клетка на карте игрока - относительно его старта, а не настоящего лабиринта:
// так никто, даже сам игрок, не знает, где он находится на самом деле
public record RaceCellModel(int X, int Y);

public record RaceWallModel(int X, int Y, int Direction);

// Карта одного игрока, которую видят все: путь, найденные стены и где он сейчас - всё от его старта
public class RacePlayerViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public bool Alive { get; set; }

    // Ник того, кто застрелил; null - жив или сам вышел
    public string? EliminatedBy { get; set; }
    public bool Left { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public List<RaceWallModel> Walls { get; set; } = [];
    public List<RaceCellModel> Visited { get; set; } = [];

    // Где лежал клад на карте этого игрока - только после конца гонки
    public RaceCellModel? Treasure { get; set; }
}

// Ответ GetRaceState: всё, что нужно клиенту, чтобы восстановить экран (в том числе после перезагрузки)
public class RaceSnapshotModel
{
    public int Bullets { get; set; }
    public bool Alive { get; set; }
    public string Turn { get; set; } = string.Empty;
    public int SecondsLeft { get; set; }
    public List<RacePlayerViewModel> Players { get; set; } = [];

    // Заполняются, когда гонка закончилась
    public string? Winner { get; set; }
    public string? WinReason { get; set; }
}
