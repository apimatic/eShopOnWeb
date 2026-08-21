using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// The boundary over the payment provider (PayPal). Every method translates provider failures
/// into <see cref="Exceptions.PaymentProcessorException"/> so callers deal with a single failure
/// type, and returns provider-neutral domain records so the SDK stays confined to Infrastructure.
///
/// Writes take an <c>idempotencyKey</c> that the processor forwards to PayPal so a repeated
/// request under the same key does not move money twice.
/// </summary>
public interface IPaymentProcessor
{
    /// <summary>Place a hold for the order total. Does not capture.</summary>
    Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Renew a stale hold on an existing order/authorization.</summary>
    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture (take the money for) a previously authorized hold.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void a hold that has not been captured, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture, fully (amount null) or partially.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Read the current hold state (status + honor-period expiry) for staleness checks.</summary>
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Vault a raw card for later reuse and return a safe descriptor. No payment is taken.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string customerReference, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// List PayPal's own record of transactions across the whole date range (every page, every
    /// sub-window), for reconciliation against local orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
