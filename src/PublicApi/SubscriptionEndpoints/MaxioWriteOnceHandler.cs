using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A retry of a Maxio write was blocked because its outcome may be unknown.")
    {
    }
}

public sealed class MaxioWriteOnceScope : IDisposable
{
    private sealed class ScopeState
    {
        public int Sends;
    }

    private static readonly AsyncLocal<ScopeState?> CurrentState = new();
    private readonly ScopeState? _prior;

    private MaxioWriteOnceScope()
    {
        _prior = CurrentState.Value;
        CurrentState.Value = new ScopeState();
    }

    public static MaxioWriteOnceScope Begin() => new();

    internal static bool TryAuthorizeSend()
    {
        var state = CurrentState.Value;
        return state is null || Interlocked.Increment(ref state.Sends) == 1;
    }

    public void Dispose() => CurrentState.Value = _prior;
}

public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !MaxioWriteOnceScope.TryAuthorizeSend())
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
