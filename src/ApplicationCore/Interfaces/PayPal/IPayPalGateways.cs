using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The PayPal payments surface this app needs (Checkout Orders v2 + Payments v2). Every operation
/// is built against the PayPal OpenAPI specification in <c>api-specs/</c>.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Creates a PayPal order for the amount and authorizes it (places a hold).</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken cancellationToken);

    /// <summary>Fetches the current state of an authorization.</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Re-authorizes a stale hold so it can still be captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Captures (takes the money on) an authorization.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, Money? amount, string idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>PayPal Vault v3 — saving and removing a shopper's cards.</summary>
public interface IPayPalVaultGateway
{
    Task<VaultedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
}

/// <summary>PayPal Transaction Search v1 — reconciliation reporting over a date range.</summary>
public interface IPayPalReportingGateway
{
    /// <summary>
    /// Returns every transaction PayPal reports for the range, paging through the whole result set
    /// (not just the first page).
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken);
}
