using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Keeps ApplicationCore free of any dependency
/// on the PayPal SDK; the implementation lives in Infrastructure.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Places a hold for the given amount, using either a raw card or a previously-saved (vaulted) card.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct);

    /// <summary>Checks whether an existing authorization is still usable for capture.</summary>
    Task<AuthorizationFreshnessResult> GetAuthorizationFreshnessAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization so it can be captured. Throws <see cref="Exceptions.PaymentAuthorizationNotRenewableException"/> if it can no longer be renewed.</summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Captures (takes) the held funds. This is the only operation that actually moves money at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Releases a hold without taking any money.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds part or all of a previously captured payment.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card for later reuse. The raw card details are sent to PayPal once and never stored by this app.</summary>
    Task<SavedCardResult> SaveCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct);

    /// <summary>Removes a previously-vaulted card so it can no longer be used to pay.</summary>
    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>Lists PayPal's own record of transactions over a date range, covering the whole range (not just the first page).</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
