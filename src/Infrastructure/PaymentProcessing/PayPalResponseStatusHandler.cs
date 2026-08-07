using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Per-request state shared between the gateway and the message handler on the current async flow.
/// The gateway sets a fresh instance into <see cref="PayPalResponseContext"/> before each SDK call;
/// the handler (running downstream in the same async context) mutates that same instance.
/// </summary>
internal sealed class PayPalResponseStatus
{
    /// <summary>The HTTP status of the response, recovered even when a later deserialization throws.</summary>
    public HttpStatusCode? StatusCode { get; set; }

    /// <summary>Whether the single permitted network send for this logical request has already happened.</summary>
    public bool Sent { get; set; }
}

internal static class PayPalResponseContext
{
    private static readonly AsyncLocal<PayPalResponseStatus?> _current = new();

    public static PayPalResponseStatus? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

/// <summary>
/// Thrown to refuse a re-send the gateway did not authorise. It deliberately does NOT derive from
/// <see cref="HttpRequestException"/> so the SDK's retry pipeline treats it as terminal rather than
/// retrying it again.
/// </summary>
internal sealed class PayPalRequestAlreadySentException : Exception
{
    public PayPalRequestAlreadySentException()
        : base("The PayPal request was already sent once on this call and will not be resent.")
    {
    }
}

/// <summary>
/// Two jobs, both required because every PayPal operation here is a non-idempotent write:
/// 1. Records each response's HTTP status so the integration boundary can classify a failure even
///    when the SDK's typed-error deserialization throws and discards the status.
/// 2. Enforces at-most-once delivery: the SDK retries transport failures on any verb, which could
///    resend a create/capture/refund/vault a second time. This blocks that resend so a lost
///    response can never become a duplicate charge or refund.
/// </summary>
internal sealed class PayPalResponseStatusHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The SDK fetches/refreshes an OAuth access token over this same pipeline. That is a separate,
        // safely-retryable request — it must not consume the single-send budget for the API operation,
        // nor overwrite the operation's response status.
        if (IsOAuthTokenRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var holder = PayPalResponseContext.Current;
        if (holder is not null)
        {
            if (holder.Sent)
            {
                // A retry of a write we already put on the wire — refuse it (outcome is unknown, not a duplicate).
                throw new PayPalRequestAlreadySentException();
            }

            holder.Sent = true;
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (holder is not null)
        {
            holder.StatusCode = response.StatusCode;
        }

        return response;
    }

    private static bool IsOAuthTokenRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        return path is not null && path.Contains("oauth2/token", StringComparison.OrdinalIgnoreCase);
    }
}
