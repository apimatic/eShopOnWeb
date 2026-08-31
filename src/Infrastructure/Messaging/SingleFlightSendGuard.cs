using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Thrown when a transport-level retry of a message-create request is blocked.
/// Derives directly from Exception (not HttpRequestException) so the SDK's retry
/// pipeline does not treat the refusal itself as retryable.
/// </summary>
public sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException(string message) : base(message) { }
}

/// <summary>
/// The SDK retries transport failures on every verb, including the non-idempotent
/// message-create POST — so a connection reset after the bytes reached the provider
/// would send the shopper the same text twice. This handler refuses any attempt of a
/// create request beyond the first within a scope the caller opens around one logical
/// send. The scope flows through the caller's async context into every retry attempt.
/// </summary>
public sealed class SingleFlightSendGuard : DelegatingHandler
{
    private static readonly AsyncLocal<SendScope?> _current = new();

    public static IDisposable BeginScope()
    {
        var scope = new SendScope();
        _current.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = _current.Value;
        if (scope != null
            && request.Method == HttpMethod.Post
            && request.RequestUri != null
            && request.RequestUri.AbsolutePath.EndsWith("/Messages.json", StringComparison.OrdinalIgnoreCase))
        {
            if (Interlocked.Increment(ref scope.Attempts) > 1)
            {
                throw new DuplicateSendBlockedException(
                    "A transport retry of this send was blocked to prevent a duplicate message; the outcome of the first attempt is unknown.");
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class SendScope : IDisposable
    {
        public int Attempts;

        public void Dispose()
        {
            if (ReferenceEquals(_current.Value, this))
            {
                _current.Value = null;
            }
        }
    }
}
