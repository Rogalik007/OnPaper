using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Collections.Concurrent;
using OnPaper.Services.LobbyService;

namespace OnPaper.Hubs;

[Authorize]
public class GameHub(ILobbyService lobbyService) : Hub
{
    private static readonly ConcurrentDictionary<string, MazeDto> games = new();

    public string GetConnectionId()
    {
        return Context.ConnectionId;
    }
    
    public async Task<int[]> GetMazeSize()
    {
        return [5, 6];
    }

    public async Task<bool> SaveMaze(MazeDto maze)
    {
        for (int i = 0; i < maze.HorizontalWalls.Length; i++)
        {
            Console.WriteLine(maze.HorizontalWalls[i]);
        }
        return true;
    }
    
    public async Task GameSearch()
    {
        await lobbyService.GameSearch(Context.UserIdentifier, Context.ConnectionId, Clients);
    }

    public async Task GameCancelSearch()
    {
        await lobbyService.GameCancelSearch(Context.UserIdentifier);
    }

    public class UserDto
    {
        public int Id { get; set; }
        public int ConnectionId { get; set; }
        public int UserId { get; set; }
        public int GameId { get; set; }
    }

    public class MazeDto
    {
        public int Id { get; set; }
        public int SizeX { get; set; }
        public int SizeY { get; set; }
        public int[] HorizontalWalls { get; set; }
        public int[] VerticalWalls { get; set; }
        public int Treasure { get; set; }
    }
}