using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Collections.Concurrent;
using OnPaper.Services.GameTreasureService;
using OnPaper.Services.LobbyService;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Hubs;

[Authorize]
public class GameHub(ILobbyService lobbyService, IGameTreasureService gameTreasureService) : Hub
{
    
    public async Task<int[]> GetMazeSize()
    {
        return [5, 6];
    }

    public async Task<bool> SaveMaze(MazeModel maze)
    {
        return await gameTreasureService.SaveMaze(Context.UserIdentifier, maze);
    }
    
    public async Task GameSearch()
    {
        await lobbyService.GameSearch(Context.UserIdentifier, Context.ConnectionId, Clients);
    }

    public async Task GameCancelSearch()
    {
        await lobbyService.GameCancelSearch(Context.UserIdentifier);
    }
}