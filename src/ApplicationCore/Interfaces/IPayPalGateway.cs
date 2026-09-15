using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin, spec-faithful abstraction over the PayPal REST APIs the integration needs:
/// Orders v2 (authorize), Payments v2 (capture / reauthorize / void / refund), Vault v3 (save / delete card)
/// and Transaction Search v1 (reconciliation). The implementation is the only place that speaks HTTP to PayPal.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and a card or vaulted-card payment source, placing a hold on
    /// the money. Throws <see cref="Exceptions.PayPalPayerActionRequiredException"/> if PayPal returns a browser
    /// (3-D Secure) challenge, and <see cref="Exceptions.PayPalApiException"/> on any other PayPal error.
    /// </summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizeOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (id, status, amount, expiry).</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, producing a fresh hold with a new id / expiry.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, PayPalMoney amount, string? requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the money for an authorization. Reports the captured amount, PayPal's fee and the net proceeds.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, PayPalMoney amount, bool finalCapture, string invoiceId, string customId, string? requestId, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before capture so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string? requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, PayPalMoney? amount, string invoiceId, string customId, string? requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for the given customer and returns the token plus a safe descriptor.</summary>
    Task<VaultedCardResult> VaultCardAsync(VaultCardRequest request, string? requestId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default);

    /// <summary>Fetches one page of PayPal's transaction records for a date window (≤ 31 days).</summary>
    Task<TransactionSearchPage> SearchTransactionsAsync(TransactionSearchQuery query, CancellationToken cancellationToken = default);
}
