using System.Text.Json.Serialization;
using OnPaper.Common.Enums;

namespace OnPaper.Common.Models.Utils;

public class MazeModel
{
    public int SizeX { get; set; }
    public int SizeY { get; set; }

    // Стенки между клетками: 0 - нет стенки, 1 - есть.
    // Горизонтальные (между клеткой и клеткой снизу): SizeX * (SizeY - 1) штук,
    // индекс стенки равен индексу верхней клетки.
    public int[] HorizontalWalls { get; set; } = [];

    // Вертикальные (между клеткой и клеткой справа): (SizeX - 1) * SizeY штук,
    // индекс стенки равен index - index / SizeX.
    public int[] VerticalWalls { get; set; } = [];

    // Индекс клетки с кладом, null - клад ещё не поставлен.
    public int? Treasure { get; set; }

    [JsonIgnore]
    public int HorizontalWallsCount => SizeX * (SizeY - 1);

    [JsonIgnore]
    public int VerticalWallsCount => (SizeX - 1) * SizeY;

    [JsonIgnore]
    public int CellsCount => SizeX * SizeY;

    // Индекс стенки в соответствующем массиве, -1 если такой стенки не существует (клетка на краю поля).
    // Логика повторяет getWallIndex из фронтового класса Maze.
    public int GetWallIndex(int cellIndex, Direction direction)
    {
        return direction switch
        {
            Direction.Up => cellIndex < SizeX ? -1 : cellIndex - SizeX,
            Direction.Right => (cellIndex + 1) % SizeX == 0 ? -1 : cellIndex - cellIndex / SizeX,
            Direction.Down => cellIndex >= SizeX * (SizeY - 1) ? -1 : cellIndex,
            Direction.Left => cellIndex % SizeX == 0 ? -1 : cellIndex - (cellIndex / SizeX + 1),
            _ => -1
        };
    }

    // Край поля тоже считается стенкой: за него ходить нельзя.
    public bool HasWall(int cellIndex, Direction direction)
    {
        if (cellIndex < 0 || cellIndex >= CellsCount)
        {
            return true;
        }

        var wallIndex = GetWallIndex(cellIndex, direction);

        if (wallIndex < 0)
        {
            return true;
        }

        var walls = direction is Direction.Right or Direction.Left ? VerticalWalls : HorizontalWalls;

        return wallIndex >= walls.Length || walls[wallIndex] == 1;
    }

    // Соседняя клетка, в которую можно перейти, или -1 если мешает стенка либо край поля.
    public int GetNeighbour(int cellIndex, Direction direction)
    {
        if (HasWall(cellIndex, direction))
        {
            return -1;
        }

        return direction switch
        {
            Direction.Up => cellIndex - SizeX,
            Direction.Right => cellIndex + 1,
            Direction.Down => cellIndex + SizeX,
            Direction.Left => cellIndex - 1,
            _ => -1
        };
    }
}
