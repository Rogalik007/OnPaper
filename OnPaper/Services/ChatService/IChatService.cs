using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.ChatService;

public interface IChatService
{
    Task<ResultModel<ChatMessageModel>> SendMessage(string? userId, string? nickname, string? gameId, string? text,
        IHubCallerClients clients);

    Task<ChatMessageModel[]> GetHistory(string? gameId);
}
