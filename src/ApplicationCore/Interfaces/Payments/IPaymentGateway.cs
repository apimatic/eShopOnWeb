using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstracts the payment processor (PayPal) so ApplicationCore never depends on its wire format.
/// Every operation is idempotent when called with the same idempotencyKey.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Authorizes (holds, does not capture) the given amount using either raw card details or a saved card's vault id.</summary>
    Task<GatewayAuthorizationResult> AuthorizeAsync(
        decimal amount, string currency, CardDetails? card, string? vaultId, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Renews (reauthorizes) a stale authorization so it can be captured again. May only succeed once per authorization, within PayPal's renewal window.</summary>
    Task<GatewayReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Captures (takes) the held funds for an authorization.</summary>
    Task<GatewayCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        bool finalCapture, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Releases a hold without taking any money.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refunds a captured payment in full (amount == null) or in part.</summary>
    Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string? note,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a card for later reuse without ever returning the full card number.</summary>
    Task<GatewaySavedCardResult> SaveCardAsync(string buyerId, CardDetails card, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>Lists PayPal's own transaction records for a date range, following pagination internally.</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
