using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using OnPaper.Services.GameTreasureService;
using OnPaper.Services.LobbyService;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Hubs;

[Authorize]
public class GameHub(ILobbyService lobbyService, IGameTreasureService gameTreasureService) : Hub
{
    
    public int[] GetMazeSize()
    {
        return gameTreasureService.GetMazeSize();
    }

    public async Task<GameSnapshotModel> GetGameState(string gameId)
    {
        var result = await gameTreasureService.GetGameState(
            Context.UserIdentifier, gameId, Context.ConnectionId, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }

        return result.Value!;
    }

    public async Task<bool> SaveMaze(string gameId, MazeModel maze)
    {
        var result = await gameTreasureService.SaveMaze(Context.UserIdentifier, gameId, maze, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }

        return result.Value;
    }

    public async Task Move(string gameId, int direction)
    {
        var result = await gameTreasureService.Move(Context.UserIdentifier, gameId, direction, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }
    }

    public async Task TurnTimeout(string gameId)
    {
        var result = await gameTreasureService.TurnTimeout(Context.UserIdentifier, gameId, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }
    }

    public async Task CheckOpponentLeft(string gameId)
    {
        var result = await gameTreasureService.CheckOpponentLeft(Context.UserIdentifier, gameId, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await gameTreasureService.PlayerLeft(Context.UserIdentifier, Clients);
        await base.OnDisconnectedAsync(exception);
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