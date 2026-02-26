using StackExchange.Redis;

namespace XcordHub.Features.Auth;

public sealed record CaptchaChallenge(string Id, string Question);

public interface ICaptchaService
{
    Task<CaptchaChallenge> GenerateAsync();
    Task<bool> ValidateAsync(string captchaId, string answer);
}

public sealed class CaptchaService(IConnectionMultiplexer redis) : ICaptchaService
{
    private static readonly Random Rng = new();

    public async Task<CaptchaChallenge> GenerateAsync()
    {
        var a = Rng.Next(1, 50);
        var b = Rng.Next(1, 50);
        var answer = (a + b).ToString();
        var question = $"{a} + {b}";
        var id = Guid.NewGuid().ToString("N");

        var db = redis.GetDatabase();
        await db.StringSetAsync($"captcha:{id}", answer, TimeSpan.FromMinutes(5));

        return new CaptchaChallenge(id, question);
    }

    public async Task<bool> ValidateAsync(string captchaId, string answer)
    {
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(answer))
            return false;

        var db = redis.GetDatabase();
        var key = $"captcha:{captchaId}";

        // Fetch and delete atomically
        var stored = await db.StringGetDeleteAsync(key);
        if (stored.IsNullOrEmpty)
            return false;

        return string.Equals(stored.ToString().Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class NoOpCaptchaService : ICaptchaService
{
    public Task<CaptchaChallenge> GenerateAsync()
        => Task.FromResult(new CaptchaChallenge("disabled", ""));

    public Task<bool> ValidateAsync(string captchaId, string answer)
        => Task.FromResult(true);
}
