using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OnPaper.Hubs;

//[Authorize]
public class GameHub : Hub
{
    private static readonly ConcurrentQueue<WaitingPlayer> queue = new();
    
    public string GetConnectionId()
    {
        return Context.ConnectionId;
    }
    
    public async Task<int[]> GetMazeSize()
    {
        Console.WriteLine(123);
        return [5, 6];
    }

    private static readonly Lock _lock = new();

    public async Task<bool> SaveMaze(MazeDto maze)
    {
        //Console.WriteLine(roomId);
        for (int i = 0; i < maze.HorizontalWalls.Length; i++)
        {
            Console.WriteLine(maze.HorizontalWalls[i]);
        }
        return true;
        //сохранение в бд
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
        Console.WriteLine(userId + " " + connectionId);
        lock (_lock)
        {
            if (queue.FirstOrDefault(user => user.UserId == userId) != null)
            {
                Console.WriteLine("bad");
                return;
            }
                
            if (queue.TryDequeue(out var existing))
            {
                opponent = existing;
            }
            else
            {
                queue.Enqueue(new WaitingPlayer(userId, connectionId));
                Console.WriteLine("good" + " " + queue.Count);
            }
        }

        if (opponent != null)
        {
            var matchId = Guid.NewGuid().ToString();

            // Уведомляем обоих игроков, что матч найден
            await Clients.Client(opponent.ConnectionId)
                .SendAsync("GameFound", matchId, userId);

            await Clients.Caller
                .SendAsync("GameFound", matchId, opponent.UserId);
        }
    }

    public Task CancelSearch()
    {
        // тут нужна логика удаления себя из очереди
        return Task.CompletedTask;
    }
    
    private record WaitingPlayer(string UserId, string ConnectionId);

    public class MazeDto
    {
        public int SizeX { get; set; }
        public int SizeY { get; set; }
        public int[] HorizontalWalls { get; set; }
        public int[] VerticalWalls { get; set; }
        public int Treasure { get; set; }
    }
}