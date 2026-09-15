using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The money-movement side of PayPal: authorize a hold, capture it at fulfilment, renew a stale
/// hold, void it, and refund a capture. Implemented in Infrastructure strictly against the PayPal
/// Orders v2 and Payments v2 OpenAPI specs. Every mutating call takes an idempotency key that the
/// implementation forwards as PayPal's <c>PayPal-Request-Id</c>, so a double-click never charges twice.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Authorize (hold) the order total. Does not take the money.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Fetch the current state of an authorization (used to detect a stale hold before capture).</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization; PayPal may return a new authorization id.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture the authorization at fulfilment; the result carries gross, PayPal fee and net proceeds.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, string invoiceReference, CancellationToken cancellationToken = default);

    /// <summary>Void the authorization before fulfilment, releasing the hold so no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string invoiceReference, CancellationToken cancellationToken = default);
}

/// <summary>The vaulting side of PayPal: save and remove a shopper's card.</summary>
public interface IPayPalVault
{
    /// <summary>Vault a card for a shopper's PayPal customer id and return a safe descriptor.</summary>
    Task<SavedCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}

/// <summary>
/// PayPal's own record of transactions for a date range, used to reconcile against eShop orders.
/// The implementation pages through the whole range (and chunks it into PayPal's allowed windows),
/// so the caller receives every transaction, not just the first page.
/// </summary>
public interface IPayPalReconciliation
{
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
