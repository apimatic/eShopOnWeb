using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin abstraction over the PayPal REST API surface this integration uses. The implementation lives in
/// Infrastructure so the domain stays free of HTTP concerns. Every call is idempotent when given the same
/// <c>requestId</c> (sent as the PayPal-Request-Id header).
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates an order with intent=AUTHORIZE and a raw card, placing a hold for the full amount.</summary>
    Task<AuthorizeResult> AuthorizeOrderWithCardAsync(Money amount, string referenceId, string invoiceId,
        string customId, CardDetails card, string requestId, CancellationToken ct = default);

    /// <summary>Creates an order with intent=AUTHORIZE paid by a previously vaulted card.</summary>
    Task<AuthorizeResult> AuthorizeOrderWithVaultedCardAsync(Money amount, string referenceId, string invoiceId,
        string customId, string vaultId, string requestId, CancellationToken ct = default);

    /// <summary>Returns the current status of an authorization (CREATED, CAPTURED, EXPIRED, VOIDED, ...).</summary>
    Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Captures (takes) an authorized amount, returning captured/fee/net as PayPal reports them.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, Money amount, string invoiceId,
        string customId, string requestId, CancellationToken ct = default);

    /// <summary>Renews a stale authorization for the given amount.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, Money amount, string requestId,
        CancellationToken ct = default);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a capture in full (amount null) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, Money? amount, string invoiceId, string? noteToPayer,
        string requestId, CancellationToken ct = default);

    /// <summary>Vaults a raw card without a purchase, returning the token and a safe descriptor.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId,
        CancellationToken ct = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default);

    /// <summary>
    /// Lists every transaction PayPal recorded within a single window. The window must be 31 days or less
    /// (PayPal's reporting limit); the implementation pages through the whole window.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset startInclusive,
        DateTimeOffset endInclusive, CancellationToken ct = default);
}
