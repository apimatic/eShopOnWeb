using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException() : base("A Maxio write retry was blocked because its outcome may already have taken effect.") { }
}

public sealed class MaxioWriteGuard
{
    private static readonly AsyncLocal<WriteState?> CurrentState = new();

    public IDisposable Begin()
    {
        if (CurrentState.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        CurrentState.Value = new WriteState();
        return new Scope();
    }

    internal static bool TryAuthorizeSend()
    {
        var state = CurrentState.Value;
        return state is null || Interlocked.Increment(ref state.SendCount) == 1;
    }

    private sealed class WriteState
    {
        public int SendCount;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => CurrentState.Value = null;
    }
}

public sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !MaxioWriteGuard.TryAuthorizeSend())
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
