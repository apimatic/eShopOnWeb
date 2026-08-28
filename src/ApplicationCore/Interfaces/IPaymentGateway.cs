using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of a payment processor. Deliberately free of any SDK type, so the domain
/// and its tests never depend on the processor's wire model.
/// Every method throws <see cref="PaymentGatewayException"/> — and nothing else — on failure.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The currency every amount is denominated in, from configuration.</summary>
    string CurrencyCode { get; }

    /// <summary>Places a hold for the full amount. No money moves.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the processor's current view of a hold.</summary>
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Renews a hold that has gone stale, returning the replacement hold.</summary>
    Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Takes the held money.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Releases a hold, so no money ever moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Returns money from a capture. A null amount means the whole remaining balance.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Stores a card in the processor's vault and returns its safe description.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Removes a card from the processor's vault.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>
    /// Every transaction the processor's reporting holds for the range — the whole range, across
    /// however many provider-side windows and pages that takes.
    /// </summary>
    Task<GatewayTransactionPage> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
