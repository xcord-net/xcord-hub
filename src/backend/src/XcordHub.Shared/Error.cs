namespace XcordHub;

public sealed record Error(string Code, string Message, int StatusCode)
{
    public static Error NotFound(string code, string message) => new(code, message, 404);

    public static Error Validation(string code, string message) => new(code, message, 400);

    public static Error BadRequest(string code, string message) => new(code, message, 400);

    public static Error Conflict(string code, string message) => new(code, message, 409);

    public static Error Forbidden(string code, string message) => new(code, message, 403);

    public static Error Failure(string code, string message) => new(code, message, 500);

    public static Error RateLimited(string code, string message) => new(code, message, 429);
}
