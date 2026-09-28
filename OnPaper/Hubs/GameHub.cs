using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using OnPaper.Services.ChatService;
using OnPaper.Services.GameTreasureService;
using OnPaper.Services.LobbyService;
using OnPaper.Services.RaceService;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Hubs;

[Authorize]
public class GameHub(
    ILobbyService lobbyService, IGameTreasureService gameTreasureService, IChatService chatService,
    IRaceService raceService)
    : Hub
{
    // Отдельного поля для ника нет - используется часть email до "@" из токена
    private string? Nickname
    {
        get
        {
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;
            return email == null ? null : NicknameUtil.FromEmail(email);
        }
    }


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

    public async Task LeaveGame(string gameId)
    {
        var result = await gameTreasureService.LeaveGame(Context.UserIdentifier, gameId, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await gameTreasureService.PlayerLeft(Context.UserIdentifier, Clients);
        await raceService.CancelSearch(Context.UserIdentifier);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task GameSearch()
    {
        await lobbyService.GameSearch(Context.UserIdentifier, Context.ConnectionId, Nickname, Clients);
    }

    public async Task GameCancelSearch()
    {
        await lobbyService.GameCancelSearch(Context.UserIdentifier);
    }

    public async Task RaceSearch()
    {
        await raceService.Search(Context.UserIdentifier, Context.ConnectionId, Nickname);
    }

    public async Task RaceCancelSearch()
    {
        await raceService.CancelSearch(Context.UserIdentifier);
    }

    public async Task<RaceSnapshotModel> GetRaceState(string gameId)
    {
        var result = await raceService.GetRaceState(Context.UserIdentifier, gameId, Context.ConnectionId);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }

        return result.Value!;
    }

    public async Task RaceMove(string gameId, int direction)
    {
        var result = await raceService.Move(Context.UserIdentifier, gameId, direction, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }
    }

    public async Task SendChatMessage(string gameId, string text)
    {
        var result = await chatService.SendMessage(Context.UserIdentifier, Nickname, gameId, text, Clients);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error!.Message);
        }
    }
}