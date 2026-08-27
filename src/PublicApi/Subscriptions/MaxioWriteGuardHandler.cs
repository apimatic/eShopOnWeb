using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScopeState?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var prior = CurrentScope.Value;
        CurrentScope.Value = new WriteScopeState();
        return new Scope(() => CurrentScope.Value = prior);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var state = CurrentScope.Value;
        if (request.Method == HttpMethod.Post &&
            state is not null &&
            Interlocked.Increment(ref state.Attempts) > 1)
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScopeState
    {
        public int Attempts;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
