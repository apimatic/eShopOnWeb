using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioWriteRetrySuppressedException : Exception
{
    public MaxioWriteRetrySuppressedException()
        : base("A second Maxio write attempt was suppressed for reconciliation.")
    {
    }
}

public sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginWrite()
    {
        var previous = CurrentScope.Value;
        var current = new WriteScope(previous);
        CurrentScope.Value = current;
        return new ScopeReleaser(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && CurrentScope.Value is { } scope && !scope.TryClaim())
        {
            throw new MaxioWriteRetrySuppressedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        private int _sendCount;

        public WriteScope(WriteScope? parent)
        {
        }

        public bool TryClaim() => Interlocked.Increment(ref _sendCount) == 1;
    }

    private sealed class ScopeReleaser : IDisposable
    {
        private readonly WriteScope? _previous;

        public ScopeReleaser(WriteScope? previous) => _previous = previous;

        public void Dispose() => CurrentScope.Value = _previous;
    }
}

public sealed class MaxioWireLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioWireLoggingHandler> _logger;

    public MaxioWireLoggingHandler(ILogger<MaxioWireLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Maxio request {Method} {Uri}", request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("Maxio response {StatusCode} for {Method} {Uri}",
            (int)response.StatusCode, request.Method, request.RequestUri);
        return response;
    }
}
