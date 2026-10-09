namespace SquareCheck;

internal sealed record OAuthCallback(string? Code, string? Error, string? State);

internal interface IRedirectListener : IDisposable
{
    Task<OAuthCallback> WaitAsync(CancellationToken ct);
    Task RespondAsync(int statusCode, string contentType, string body, CancellationToken ct);
}
