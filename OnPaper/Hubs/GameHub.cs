using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Collections.Concurrent;

namespace OnPaper.Hubs;

[Authorize]
public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, WaitingPlayer> waitingPlayers = new();
    private static readonly ConcurrentDictionary<string, MazeDto> games = new();
    private static readonly Random random = new();

    public string GetConnectionId()
    {
        return Context.ConnectionId;
    }
    
    public async Task<int[]> GetMazeSize()
    {
        return [5, 6];
    }

    private static readonly Lock _lock = new();

    public async Task<bool> SaveMaze(MazeDto maze)
    {
        for (int i = 0; i < maze.HorizontalWalls.Length; i++)
        {
            Console.WriteLine(maze.HorizontalWalls[i]);
        }
        return true;
    }
    
    public async Task FindGame()
    {
        var userId = Context.UserIdentifier;
        
        var connectionId = Context.ConnectionId;

        if (userId == null || connectionId == null)
        {
            return;
        }

        WaitingPlayer? opponent = null;
        string? opponentId = null;

        lock (_lock)
        {
            if (waitingPlayers.ContainsKey(userId))
            {
                throw new HubException("You are already in queue");
            }

            if (!waitingPlayers.IsEmpty)
            {
                var keys = waitingPlayers.Keys.ToList();
                opponentId = keys[random.Next(keys.Count)];
                opponent = waitingPlayers[opponentId];
            }
            else
            {
                waitingPlayers.TryAdd(userId, new WaitingPlayer(userId, 80));
            }
        }

        if (opponent != null && opponentId != null)
        {
            var gameId = Guid.NewGuid().ToString();

            // Уведомляем обоих игроков, что матч найден
            await Clients.Client(opponent.ConnectionId)
                .SendAsync("GameFound", gameId, userId);

            await Clients.Caller
                .SendAsync("GameFound", gameId, opponentId);

            waitingPlayers.TryRemove(userId, out _);
            waitingPlayers.TryRemove(opponentId, out _);
        }
    }

    public Task CancelSearch()
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
        {
            waitingPlayers.TryRemove(userId, out _);
        }
        return Task.CompletedTask;
    }
    
    private record WaitingPlayer(string ConnectionId, int rating);

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