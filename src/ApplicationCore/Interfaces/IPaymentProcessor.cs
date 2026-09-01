using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Implementations translate these
/// operations onto the processor's API contract. All mutating operations take an
/// idempotency key which the processor uses to deduplicate retries.
/// </summary>
public interface IPaymentProcessor
{
    /// <summary>Places a hold on the given amount (authorize, not capture).</summary>
    Task<ProcessorAuthorization> AuthorizeAsync(decimal amount, string currency, PaymentSourceSelection source,
        string merchantReference, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current processor-side state of an authorization.</summary>
    Task<ProcessorAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization.</summary>
    Task<ProcessorAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Takes the money for a previously authorized payment.</summary>
    Task<ProcessorCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without moving money.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<ProcessorRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string? note, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Saves a card in the processor's vault for the given customer.</summary>
    Task<ProcessorVaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a card from the processor's vault.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>Lists the processor's own record of transactions over the whole range (all pages).</summary>
    Task<IReadOnlyList<ProcessorTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
