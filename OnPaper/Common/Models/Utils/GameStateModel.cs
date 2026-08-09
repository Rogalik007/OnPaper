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

    // UserId нашедшего клад, null - партия ещё идёт
    public string? Winner { get; set; }

    // Стенки, в которые игрок уже упирался. Нужны, чтобы после перезагрузки
    // страницы открытая часть чужого лабиринта не потерялась.
    public Dictionary<string, List<WallHitModel>> DiscoveredWalls { get; set; } = new();

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
}

public record WallHitModel(int Cell, int Direction);
