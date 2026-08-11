using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// The application's boundary to PayPal. Every method translates provider and transport failures into a
/// single <see cref="PaymentGatewayException"/> (or <see cref="PaymentApprovalRequiredException"/>), so
/// callers never see SDK types. Mutating calls take a stable idempotency key that is sent to PayPal as the
/// PayPal-Request-Id header, so a retried request does not act twice.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Create an order (intent=AUTHORIZE) paid by a raw card and place a hold for the amount.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string orderReference, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Create an order (intent=AUTHORIZE) paid by a vaulted (saved) card and place a hold.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultAsync(decimal amount, string currency, string vaultId,
        string orderReference, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renew a stale authorization so it can still be captured; returns the renewed hold.</summary>
    Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture (take) an authorized payment. The result carries gross, PayPal fee and net proceeds.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void an authorization, releasing the held funds. No money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault a card for reuse; returns the vault id and a safe descriptor (brand, last four, expiry).</summary>
    Task<VaultResult> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Remove a card from the vault so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>List PayPal's own transaction records across the whole date range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
