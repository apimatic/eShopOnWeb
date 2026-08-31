using System.Collections.Concurrent;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class DuplicateProviderWriteBlockedException : Exception
{
    public DuplicateProviderWriteBlockedException()
        : base("A repeated provider write was blocked because its outcome is unknown.") { }
}

internal static class ProviderWriteScope
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, byte>?> CurrentScope = new();

    public static IDisposable Begin()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        return new Scope(previous);
    }

    public static bool TryClaim(string key)
    {
        var scope = CurrentScope.Value;
        return scope is null || scope.TryAdd(key, 0);
    }

    private sealed class Scope(ConcurrentDictionary<string, byte>? previous) : IDisposable
    {
        public void Dispose() => CurrentScope.Value = previous;
    }
}

public sealed class PayPalWriteGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isWrite = request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete ||
                      request.Method == HttpMethod.Patch;
        var isTokenRequest = request.RequestUri?.AbsolutePath.EndsWith("/v1/oauth2/token",
            StringComparison.OrdinalIgnoreCase) == true;

        if (isWrite && !isTokenRequest)
        {
            var requestId = request.Headers.TryGetValues("PayPal-Request-Id", out var values)
                ? values.FirstOrDefault()
                : null;
            var key = $"{request.Method}:{request.RequestUri?.AbsolutePath}:{requestId}";
            if (!ProviderWriteScope.TryClaim(key))
            {
                throw new DuplicateProviderWriteBlockedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
