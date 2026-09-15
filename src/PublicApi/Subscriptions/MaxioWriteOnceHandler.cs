using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Prevents the SDK's transport retry pipeline from sending a non-idempotent POST twice.
/// The service reconciles the one allowed write by reading provider state after a failure.
/// </summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private sealed class WriteScope
    {
        public int Sends;
    }

    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable Begin()
    {
        var previous = Current.Value;
        Current.Value = new WriteScope();
        return new Scope(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = Current.Value;
        if (scope is not null && request.Method == HttpMethod.Post && Interlocked.Exchange(ref scope.Sends, 1) == 1)
        {
            throw new MaxioWriteAlreadySentException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class Scope : IDisposable
    {
        private readonly WriteScope? _previous;
        private bool _disposed;

        public Scope(WriteScope? previous) => _previous = previous;

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
