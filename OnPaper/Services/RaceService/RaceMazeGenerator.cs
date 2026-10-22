using System;
using System.Collections.Generic;
using System.Linq;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.RaceService;

// Случайный лабиринт для гонки. Сначала поиском в глубину строится "идеальный" лабиринт -
// из любой клетки в любую ровно один путь. В нём негде разминуться и стрелять можно только
// вдоль тупиков, поэтому затем часть оставшихся стен сносится, чтобы появились петли.
public static class RaceMazeGenerator
{
    // Доля внутренних стен, которые сносятся после построения идеального лабиринта
    private const double EXTRA_PASSAGES = 0.15;

    private static readonly Direction[] AllDirections =
        [Direction.Up, Direction.Right, Direction.Down, Direction.Left];

    public static MazeModel Generate(int sizeX, int sizeY, Random random)
    {
        var maze = new MazeModel
        {
            SizeX = sizeX,
            SizeY = sizeY,
            HorizontalWalls = Enumerable.Repeat(1, sizeX * (sizeY - 1)).ToArray(),
            VerticalWalls = Enumerable.Repeat(1, (sizeX - 1) * sizeY).ToArray(),
            Mines = []
        };

        var visited = new bool[maze.CellsCount];
        var stack = new Stack<int>();
        var start = random.Next(maze.CellsCount);

        visited[start] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cell = stack.Peek();
            var next = AllDirections
                .Where(d => maze.GetWallIndex(cell, d) >= 0 && !visited[Step(maze, cell, d)])
                .ToArray();

            if (next.Length == 0)
            {
                stack.Pop();
                continue;
            }

            var direction = next[random.Next(next.Length)];
            var neighbour = Step(maze, cell, direction);

            RemoveWall(maze, cell, direction);
            visited[neighbour] = true;
            stack.Push(neighbour);
        }

        var remaining = new List<(int Cell, Direction Direction)>();

        for (var cell = 0; cell < maze.CellsCount; cell++)
        {
            foreach (var direction in new[] { Direction.Right, Direction.Down })
            {
                if (maze.GetWallIndex(cell, direction) >= 0 && maze.HasWall(cell, direction))
                {
                    remaining.Add((cell, direction));
                }
            }
        }

        foreach (var (cell, direction) in remaining.OrderBy(_ => random.Next())
                     .Take((int)(remaining.Count * EXTRA_PASSAGES)))
        {
            RemoveWall(maze, cell, direction);
        }

        return maze;
    }

    // Кратчайшие расстояния по проходам от клетки from до всех остальных
    public static int[] Distances(MazeModel maze, int from)
    {
        var distances = Enumerable.Repeat(-1, maze.CellsCount).ToArray();
        var queue = new Queue<int>();

        distances[from] = 0;
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();

            foreach (var direction in AllDirections)
            {
                var next = maze.GetNeighbour(cell, direction);

                if (next < 0 || distances[next] >= 0)
                {
                    continue;
                }

                distances[next] = distances[cell] + 1;
                queue.Enqueue(next);
            }
        }

        return distances;
    }

    // Соседняя клетка без учёта стен (вызывается только для направлений внутрь поля)
    private static int Step(MazeModel maze, int cell, Direction direction) => direction switch
    {
        Direction.Up => cell - maze.SizeX,
        Direction.Right => cell + 1,
        Direction.Down => cell + maze.SizeX,
        _ => cell - 1
    };

    private static void RemoveWall(MazeModel maze, int cell, Direction direction)
    {
        var index = maze.GetWallIndex(cell, direction);

        if (direction is Direction.Right or Direction.Left)
        {
            maze.VerticalWalls[index] = 0;
        }
        else
        {
            maze.HorizontalWalls[index] = 0;
        }
    }
}
