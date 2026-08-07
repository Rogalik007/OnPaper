using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.GameTreasureService;

public class GameTreasureService : IGameTreasureService
{
    public async Task<bool> SaveMaze(string? userId, MazeModel maze)
    {
        /*for (int i = 0; i < maze.HorizontalWalls.Length; i++)
        {
            Console.WriteLine(maze.HorizontalWalls[i]);
        }*/
        return true;
    }
}