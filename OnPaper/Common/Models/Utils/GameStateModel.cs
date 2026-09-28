using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OnPaper.Common.Models.Utils;

public class GameStateModel
{
    // UserId игрока, который сейчас ходит
    public string Turn { get; set; } = string.Empty;

    // Момент, после которого ход считается просроченным
    public DateTimeOffset TurnDeadline { get; set; }

    // UserId -> клетка, на которой игрок стоит в лабиринте противника
    public Dictionary<string, int> Positions { get; set; } = new();

    // UserId победителя, null - партия ещё идёт
    public string? Winner { get; set; }

    // Победа засчитана из-за того, что противник не вернулся, а не за найденный клад
    public bool EndedByDisconnect { get; set; }

    // Стенки, в которые игрок уже упирался. Нужны, чтобы после перезагрузки
    // страницы открытая часть чужого лабиринта не потерялась.
    public Dictionary<string, List<WallHitModel>> DiscoveredWalls { get; set; } = new();

    // Мины и супер-мины, на которые игрок уже наступил в чужом лабиринте.
    // Нужны и для отрисовки "М"/"С" на поле, и чтобы не потерять их после перезагрузки страницы.
    public Dictionary<string, List<MineHitModel>> DiscoveredMines { get; set; } = new();

    // UserId -> сколько ходов игрок должен пропустить из-за мин. Списывается при передаче хода:
    // вместо игрока с ненулевым счётчиком ходит противник, а счётчик уменьшается на 1.
    public Dictionary<string, int> SkipTurns { get; set; } = new();

    [JsonIgnore]
    public bool IsTurnExpired => DateTimeOffset.UtcNow > TurnDeadline;

    public int GetPosition(string userId) => Positions.GetValueOrDefault(userId);

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

    public List<MineHitModel> GetDiscoveredMines(string userId)
        => DiscoveredMines.TryGetValue(userId, out var mines) ? mines : [];

    public void AddDiscoveredMine(string userId, int cell, bool isSuper)
    {
        if (!DiscoveredMines.TryGetValue(userId, out var mines))
        {
            mines = [];
            DiscoveredMines[userId] = mines;
        }

        if (!mines.Any(m => m.Cell == cell))
        {
            mines.Add(new MineHitModel(cell, isSuper));
        }
    }

    // Ходы копятся, а не сжигаются сразу: игрок может наступить на несколько мин
    // до того, как очередь снова дойдёт до него.
    public void AddSkipTurns(string userId, int count)
        => SkipTurns[userId] = SkipTurns.GetValueOrDefault(userId) + count;

    // Кому переходит ход после того, как sourceUserId сходил: обычно - противнику,
    // но если у него накоплены пропуски ходов от мин, они списываются один за другим,
    // и очередь может вернуться обратно к sourceUserId.
    public string GetNextTurn(string sourceUserId, string opponentUserId)
    {
        var next = opponentUserId;

        while (SkipTurns.GetValueOrDefault(next) > 0)
        {
            SkipTurns[next]--;
            next = next == opponentUserId ? sourceUserId : opponentUserId;
        }

        return next;
    }
}

public record WallHitModel(int Cell, int Direction);

public record MineHitModel(int Cell, bool IsSuper);
