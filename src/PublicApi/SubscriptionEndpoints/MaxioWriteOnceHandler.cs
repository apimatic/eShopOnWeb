using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("A retry of a Maxio write was blocked because the first attempt may have succeeded.")
    {
    }
}

public sealed class MaxioWriteScope : IDisposable
{
    private static readonly AsyncLocal<WriteState?> CurrentState = new();
    private readonly WriteState? _prior;
    private bool _disposed;

    private MaxioWriteScope()
    {
        _prior = CurrentState.Value;
        CurrentState.Value = new WriteState();
    }

    internal static WriteState? Current => CurrentState.Value;

    public static MaxioWriteScope Begin() => new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CurrentState.Value = _prior;
        _disposed = true;
    }

    internal sealed class WriteState
    {
        public int SendCount;
    }
}

public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var state = MaxioWriteScope.Current;
        if (state is not null && request.Method == HttpMethod.Post &&
            Interlocked.Increment(ref state.SendCount) > 1)
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
