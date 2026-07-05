using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OnPaper.Hubs;

[Authorize]
public class GameHub : Hub
{
    private static readonly ConcurrentQueue<WaitingPlayer> queue = new();
    
    public async Task SendMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }
    
    public string GetConnectionId()
    {
        return Context.ConnectionId;
    }
    
    private static readonly Lock _lock = new();

    public async Task SaveMaze(string roomId, byte[] maze)
    {
        Console.WriteLine(roomId);
    }
    
    public async Task FindGame()
    {
        var userId = Context.UserIdentifier;
        
        var connectionId = Context.ConnectionId;

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
}