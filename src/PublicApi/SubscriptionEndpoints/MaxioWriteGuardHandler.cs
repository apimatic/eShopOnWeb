using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioWriteReplayException : Exception
{
    public MaxioWriteReplayException(string operation)
        : base($"The billing write was prevented from being replayed: {operation}.")
    {
    }
}

internal static class MaxioWriteSendScope
{
    private static readonly AsyncLocal<WriteState?> Current = new();

    public static IDisposable Begin(string operation)
    {
        var previous = Current.Value;
        Current.Value = new WriteState(operation);
        return new Scope(previous);
    }

    public static bool TryMarkSent(out string operation)
    {
        var state = Current.Value;
        operation = state?.Operation ?? string.Empty;
        if (state == null)
        {
            return true;
        }

        if (state.Sent)
        {
            return false;
        }

        state.Sent = true;
        return true;
    }

    private sealed class WriteState
    {
        public WriteState(string operation) => Operation = operation;
        public string Operation { get; }
        public bool Sent { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly WriteState? _previous;
        private bool _disposed;

        public Scope(WriteState? previous) => _previous = previous;

        public void Dispose()
        {
            if (!_disposed)
            {
                Current.Value = _previous;
                _disposed = true;
            }
        }
    }
}

internal sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !MaxioWriteSendScope.TryMarkSent(out var operation))
        {
            throw new MaxioWriteReplayException(operation);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
