using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Strips the "Idempotency-Key" header the PayPal .NET SDK (AsadAli.Checkout.Sdk v1.0.1) attaches
/// unconditionally to every mutating call (Orders, Payments, Vault, Subscriptions, and even the
/// OAuth2 token request) -- it is hardcoded per call site as `new HeaderParam("Idempotency-Key",
/// Guid.NewGuid())` inside the SDK's generated request builders (confirmed in source: Api/Orders.cs,
/// Api/Payments.cs, Api/Vault.cs, Api/Subscriptions.cs, and the OAuth2 client-credentials/authorization-
/// code/password token strategies). It is not exposed by PayPalServerSdkClientOptions or per-call
/// RequestOptions (RequestOptions only carries LogLevel) -- there is no supported knob to suppress it,
/// so it is removed here at the HttpClient pipeline level instead. This header is not part of
/// PayPal's own documented request shape for these endpoints (only "PayPal-Request-Id" is), so
/// removing it keeps the wire request aligned with what PayPal's own API reference describes.
/// </summary>
public class PayPalIdempotencyKeyStrippingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Remove("Idempotency-Key");
        return base.SendAsync(request, ct);
    }
}
