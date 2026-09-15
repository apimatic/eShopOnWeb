using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the PayPal Orders v2 + Payments v2 APIs for the authorize / capture / reauthorize
/// / void / refund flow. Implemented in the infrastructure layer against the PayPal OpenAPI contract.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE and places a hold for the total.</summary>
    Task<GatewayAuthorization> AuthorizeOrderAsync(AuthorizeGatewayRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) a previously authorized amount.</summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale hold. PayPal may return a new authorization id.</summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before capture, so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of a hold from PayPal.</summary>
    Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
}
