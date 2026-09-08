using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown by <see cref="WriteOnceHttpMessageHandler"/> when a retry attempts to
/// re-send a mutating request that has already been sent once within the current
/// scope. Deliberately NOT an <see cref="HttpRequestException"/> so the SDK's retry
/// pipeline never treats the refusal itself as retryable.
/// </summary>
public sealed class WriteResendBlockedException : Exception
{
    public WriteResendBlockedException()
        : base("A mutating request was already sent in this scope; refusing to resend it.")
    {
    }
}

/// <summary>
/// A <see cref="DelegatingHandler"/> that guarantees each mutating request is sent
/// to the network at most once per <see cref="BeginScope"/>.
///
/// The APIMatic SDK retry pipeline resends <c>POST</c>/<c>PUT</c>/<c>PATCH</c>/<c>DELETE</c>
/// requests when a transport failure occurs (the request may already have reached the
/// provider). Idempotency at the application layer cannot rely on Maxio rejecting a
/// duplicate, so the first send is allowed and every later attempt in the same scope is
/// refused with <see cref="WriteResendBlockedException"/>. The caller then reconciles
/// by re-reading provider state instead of assuming the write did not happen.
/// </summary>
public sealed class WriteOnceHttpMessageHandler : DelegatingHandler
{
    private static readonly AsyncLocal<bool> _writeSent = new AsyncLocal<bool>();

    /// <summary>
    /// Opens a scope in which at most one mutating request may reach the network.
    /// Every <see cref="WriteOnceHttpMessageHandler"/> instance shares the scope.
    /// </summary>
    public static IDisposable BeginScope() => new WriteScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsMutating(request.Method))
        {
            if (_writeSent.Value)
            {
                throw new WriteResendBlockedException();
            }

            _writeSent.Value = true;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsMutating(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;

    private sealed class WriteScope : IDisposable
    {
        private readonly bool _previous;
        private int _disposed;

        public WriteScope()
        {
            _previous = _writeSent.Value;
            _writeSent.Value = false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _writeSent.Value = _previous;
        }
    }
}
