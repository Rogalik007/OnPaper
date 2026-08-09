using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace OnPaper.Services.LobbyService;

public interface ILobbyService
{
    Task GameSearch(string? userId, string? connectionId, IHubCallerClients clients);
    Task GameCancelSearch(string? userId);
}
