using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal's REST APIs. The concrete implementation lives in Infrastructure and is the
/// only place that talks HTTP to PayPal. All inputs/outputs are plain domain DTOs so ApplicationCore
/// stays free of transport concerns. Raw card data flows through here but is never persisted or logged.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold on <paramref name="amount"/> without taking it (intent=AUTHORIZE), using either a raw
    /// card or a vaulted card. Idempotent on <paramref name="requestId"/>.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, PayPalCardInstrument instrument,
        string requestId, string customId, CancellationToken cancellationToken);

    /// <summary>Reads the current state of an authorization (status and expiry).</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Refreshes a hold that has gone stale. Throws <see cref="PayPalApiException"/> if it can no longer be renewed.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Takes the money for a held authorization. Idempotent on <paramref name="requestId"/>.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string customId,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Releases a hold without charging.</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Refunds a capture, fully (<paramref name="amount"/> null) or partially. Idempotent on
    /// <paramref name="requestId"/> (the caller-supplied idempotency key).
    /// </summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string customId,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Vaults a card for later reuse and returns a safe description plus the vault token id.</summary>
    Task<PayPalVaultedCardResult> VaultCardAsync(PayPalRawCard card, string? customerId,
        string requestId, CancellationToken cancellationToken);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole [from, to] range, transparently
    /// chunking the range into PayPal's 31-day windows and following every page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
