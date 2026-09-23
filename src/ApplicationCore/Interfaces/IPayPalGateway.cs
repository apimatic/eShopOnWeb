using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over PayPal. One method per PayPal operation in scope; the orchestration
/// service owns the sequencing and idempotency. Every method translates provider failures into a
/// <see cref="Exceptions.PayPalGatewayException"/> (or, for a browser-approval challenge, a
/// <see cref="Exceptions.PaymentChallengeRequiredException"/>).
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order with intent = AUTHORIZE. <paramref name="requestId"/> is the idempotency key.</summary>
    Task<PayPalOrderResult> CreateOrderAsync(CreatePayPalOrderRequest request, string requestId, CancellationToken ct);

    /// <summary>Authorizes (places a hold on) a created PayPal order. <paramref name="requestId"/> is the idempotency key.</summary>
    Task<AuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken ct);

    /// <summary>Re-reads a PayPal order (used to settle an unknown outcome after a transport failure).</summary>
    Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId, CancellationToken ct);

    /// <summary>Captures an authorized payment in full. <paramref name="requestId"/> is the idempotency key.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Re-reads an authorization (used to settle an unknown outcome, and to check for expiry).</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Re-authorizes a stale authorization, yielding a fresh hold. <paramref name="requestId"/> is the idempotency key.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task<VoidResult> VoidAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full (null amount) or in part. <paramref name="idempotencyKey"/> dedupes.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, string? invoiceId, CancellationToken ct);

    /// <summary>Vaults a card and returns its vault id plus a safe descriptor.</summary>
    Task<VaultCardResult> CreateVaultCardAsync(VaultCardRequest request, string requestId, CancellationToken ct);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultCardAsync(string vaultId, CancellationToken ct);

    /// <summary>Reads one page of PayPal's own transaction record for a date range (RFC-3339 date-times).</summary>
    Task<TransactionSearchPage> SearchTransactionsAsync(string startDate, string endDate, int page,
        int pageSize, CancellationToken ct);
}
