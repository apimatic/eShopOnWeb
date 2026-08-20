using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteGuard
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public IDisposable Begin()
    {
        if (CurrentScope.Value != null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        CurrentScope.Value = new WriteScope();
        return new ScopeLease();
    }

    public bool TryBeginSend()
    {
        var scope = CurrentScope.Value;
        return scope == null || Interlocked.Increment(ref scope.SendCount) == 1;
    }

    private sealed class WriteScope
    {
        public int SendCount;
    }

    private sealed class ScopeLease : IDisposable
    {
        public void Dispose() => CurrentScope.Value = null;
    }
}

internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private readonly MaxioWriteGuard _guard;
    private readonly ILogger<MaxioWriteOnceHandler> _logger;

    public MaxioWriteOnceHandler(MaxioWriteGuard guard, ILogger<MaxioWriteOnceHandler> logger)
    {
        _guard = guard;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !_guard.TryBeginSend())
        {
            throw new MaxioWriteReplayBlockedException();
        }

        _logger.LogDebug("Maxio request {Method} {Path}", request.Method, request.RequestUri?.AbsolutePath);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogDebug("Maxio response {StatusCode} for {Method} {Path}",
            (int)response.StatusCode, request.Method, request.RequestUri?.AbsolutePath);
        return response;
    }
}

internal sealed class MaxioWriteReplayBlockedException : Exception
{
}
