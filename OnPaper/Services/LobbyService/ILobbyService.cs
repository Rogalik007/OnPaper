using StackExchange.Redis;

namespace OnPaper.Services.LobbyService;

public interface ILobbyService
{
    Task<string?> GetValueAsync(string key);
    Task SetValueAsync(string key, string value, TimeSpan? expiry = null);
    Task<bool> KeyExistsAsync(string key);
    Task<bool> DeleteKeyAsync(string key);
    IDatabase Database { get; }
}
