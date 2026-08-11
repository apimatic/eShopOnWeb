using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// The money-movement operations we need from PayPal, expressed in the app's own terms.
/// The concrete implementation builds against PayPal's OpenAPI contract (Checkout Orders v2
/// for the hold, Payments v2 for capture / void / reauthorize / refund).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Places a hold on the order total (authorize, not capture). Encapsulates creating the
    /// PayPal order (intent=AUTHORIZE) with the given card or vaulted card and authorizing it.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of a hold (used to detect a stale authorization).</summary>
    Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale hold. PayPal returns a new authorization id.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Takes the money for a hold (capture at fulfilment).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before capture. No money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string? invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);
}
