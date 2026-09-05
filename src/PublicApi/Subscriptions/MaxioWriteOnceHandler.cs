using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Prevents the SDK transport-retry policy from sending a guarded POST twice.</summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope(previous);
        return CurrentScope.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && CurrentScope.Value is { } scope && !scope.TryRegisterSend())
            throw new MaxioWriteRetrySuppressedException();

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        private readonly WriteScope? _previous;
        private int _sends;

        public WriteScope(WriteScope? previous) => _previous = previous;
        public bool TryRegisterSend() => Interlocked.Increment(ref _sends) == 1;
        public void Dispose() => CurrentScope.Value = _previous;
    }
}

public sealed class MaxioWriteRetrySuppressedException : Exception
{
    public MaxioWriteRetrySuppressedException() : base("A duplicate Maxio write was suppressed.") { }
}
