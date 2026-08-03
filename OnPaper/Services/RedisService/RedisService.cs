using StackExchange.Redis;

namespace OnPaper.Services.RedisService;

public class RedisService: IRedisService
{
    private readonly IDatabase _db;

    public RedisService(IConnectionMultiplexer muxer)
    {
        _db = muxer.GetDatabase();
    }

    public IDatabase Database => _db;

    public async Task<string?> GetValueAsync(string key)
    {
        try
        {
            return await _db.StringGetAsync(key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetValueAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            await _db.StringSetAsync(key, value, expiry, true);
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<bool> KeyExistsAsync(string key) => await _db.KeyExistsAsync(key);
    public async Task<bool> DeleteKeyAsync(string key) => await _db.KeyDeleteAsync(key);
}
