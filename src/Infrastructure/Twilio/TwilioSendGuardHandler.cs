using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The SDK's retry pipeline resends a request on any verb after a transport failure, so a
/// non-idempotent write (creating a message) could reach the provider twice — a duplicate SMS.
/// This handler refuses a second attempt of a message-create POST within a write scope; the
/// blocked attempt never reaches the network, keeping the send count at one. The scope lives in
/// an AsyncLocal (retries run inside the caller's async context); the count is taken BEFORE the
/// request goes out, because a request that failed on the way out may still have been received.
/// </summary>
public sealed class TwilioSendGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<SendGuardScope?> CurrentScope = new();

    public static IDisposable BeginWriteScope()
    {
        var scope = new SendGuardScope();
        CurrentScope.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope != null
            && request.Method == HttpMethod.Post
            && request.RequestUri != null
            && request.RequestUri.AbsolutePath.EndsWith("/Messages.json", StringComparison.OrdinalIgnoreCase))
        {
            if (scope.SendAttempted)
            {
                // A private sentinel — deliberately NOT HttpRequestException, which the pipeline would retry.
                throw new TwilioDuplicateSendGuardException();
            }

            scope.SendAttempted = true;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class SendGuardScope : IDisposable
    {
        public bool SendAttempted;

        public void Dispose()
        {
            if (CurrentScope.Value == this)
            {
                CurrentScope.Value = null;
            }
        }
    }
}

/// <summary>Thrown when a retry pipeline attempts a second send of the same message-create request.</summary>
public sealed class TwilioDuplicateSendGuardException : Exception
{
    public TwilioDuplicateSendGuardException()
        : base("A duplicate send of the same message-create request was blocked.")
    {
    }
}
