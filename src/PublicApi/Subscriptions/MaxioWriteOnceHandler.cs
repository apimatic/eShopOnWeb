using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Stops SDK transport retries from issuing a second externally visible POST in one write scope.</summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();
    private readonly ILogger<MaxioWriteOnceHandler> _logger;

    public MaxioWriteOnceHandler(ILogger<MaxioWriteOnceHandler> logger) => _logger = logger;

    public static IDisposable BeginWrite()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope();
        return new ScopeLease(previous);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && !scope.TryRegisterSend())
            throw new MaxioWriteRetryBlockedException();

        _logger.LogDebug("Maxio request {Method} {Uri}", request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogDebug("Maxio response {StatusCode} for {Method} {Uri}", (int)response.StatusCode, request.Method, request.RequestUri);
        return response;
    }

    private sealed class WriteScope
    {
        private int _sendCount;
        public bool TryRegisterSend() => Interlocked.Increment(ref _sendCount) == 1;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly WriteScope? _previous;
        public ScopeLease(WriteScope? previous) => _previous = previous;
        public void Dispose() => CurrentScope.Value = _previous;
    }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException() : base("The Maxio write may have completed; its outcome must be reconciled.") { }
}
