using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScopeState?> CurrentScope = new();

    public static IDisposable BeginWriteScope()
    {
        if (CurrentScope.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        CurrentScope.Value = new WriteScopeState();
        return new ScopeReleaser();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && Interlocked.Increment(ref scope.SendCount) > 1)
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScopeState
    {
        public int SendCount;
    }

    private sealed class ScopeReleaser : IDisposable
    {
        public void Dispose() => CurrentScope.Value = null;
    }
}

internal sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("A replay of a non-idempotent Maxio write was blocked.")
    {
    }
}
