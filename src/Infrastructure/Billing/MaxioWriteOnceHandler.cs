using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteAlreadyAttemptedException : Exception
{
    public MaxioWriteAlreadyAttemptedException()
        : base("The guarded Maxio write was not sent again.")
    {
    }
}

internal static class MaxioWriteOnceScope
{
    private static readonly AsyncLocal<ScopeState?> CurrentState = new();

    public static IDisposable Begin()
    {
        if (CurrentState.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write-once scope is already active.");
        }

        var state = new ScopeState();
        CurrentState.Value = state;
        return new ScopeLease(state);
    }

    public static bool ShouldBlock(HttpRequestMessage request)
    {
        var state = CurrentState.Value;
        return state is not null &&
               request.Method == HttpMethod.Post &&
               Interlocked.Increment(ref state.SendCount) > 1;
    }

    private sealed class ScopeState
    {
        public int SendCount;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly ScopeState _state;

        public ScopeLease(ScopeState state)
        {
            _state = state;
        }

        public void Dispose()
        {
            if (ReferenceEquals(CurrentState.Value, _state))
            {
                CurrentState.Value = null;
            }
        }
    }
}

internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (MaxioWriteOnceScope.ShouldBlock(request))
        {
            throw new MaxioWriteAlreadyAttemptedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
