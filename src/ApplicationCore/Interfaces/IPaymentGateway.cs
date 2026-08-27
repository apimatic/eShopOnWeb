using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Every operation accepts an
/// idempotency key so a retried request never moves money twice.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency every charge is made in, from configuration.</summary>
    string Currency { get; }

    /// <summary>Authorize (hold) an amount using full card details supplied for a one-off payment.</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, string customId, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) an amount using a previously vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId,
        string idempotencyKey, string customId, string invoiceId, CancellationToken cancellationToken = default);

    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew an authorization whose honor period has lapsed.</summary>
    Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) money against an authorization.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Release a hold without moving money.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Vault a card for later use. Full card details go to the provider only.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>The provider's own record of transactions over a date range (all pages).</summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
