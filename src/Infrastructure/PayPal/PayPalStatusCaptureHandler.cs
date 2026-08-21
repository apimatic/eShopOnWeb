using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Per-call state used to carry the last HTTP status back out of the SDK's handler pipeline.</summary>
internal sealed class PayPalCallScope
{
    public int? StatusCode;
}

/// <summary>
/// A scope object is set on this AsyncLocal before each SDK call. The reference flows DOWN into the
/// handler, which mutates it — so the caller can read the status after the call, even when the SDK
/// throws a JsonException that would otherwise discard the HTTP status.
/// </summary>
internal static class PayPalCallContext
{
    public static readonly System.Threading.AsyncLocal<PayPalCallScope?> Current = new();
}

/// <summary>
/// Records the HTTP status of the last response on the ambient <see cref="PayPalCallScope"/>. This is
/// the SDK's own advice for a new integration — and here it also gives the error boundary the real
/// status, which the typed error models alone do not expose.
/// </summary>
internal sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var scope = PayPalCallContext.Current.Value;
        if (scope is not null)
            scope.StatusCode = (int)response.StatusCode;
        return response;
    }
}
