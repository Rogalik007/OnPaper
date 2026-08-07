using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.GameTreasureService;

public interface IGameTreasureService
{
    Task<bool> SaveMaze(string? userId, MazeModel maze);
}