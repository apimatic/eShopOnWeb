using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. The concrete implementation lives in the
/// Infrastructure layer so that ApplicationCore never depends on the PayPal SDK. All amounts are
/// expressed in <see cref="Currency"/>. Implementations translate provider failures into the
/// domain payment exceptions and never leak SDK types or card details.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Configured settlement currency (ISO-4217), sourced from PayPal:Currency.</summary>
    string Currency { get; }

    /// <summary>
    /// Authorize (hold) <paramref name="amount"/> against a card. Supply either <paramref name="card"/>
    /// for a one-off payment or <paramref name="vaultId"/> to charge a saved card — not both.
    /// <paramref name="idempotencyKey"/> makes a repeat call a no-op on PayPal's side.
    /// </summary>
    Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency, CardDetails? card,
        string? vaultId, string idempotencyKey, string? customId, CancellationToken cancellationToken);

    /// <summary>Capture a held authorization at fulfilment; the result carries the captured amount, fee and net.</summary>
    Task<PayPalCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Renew a stale authorization so it can still be captured. Throws when it can no longer be renewed.</summary>
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken);

    /// <summary>Void a held authorization, releasing the shopper's funds. No money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Refund a captured payment in full or in part. <paramref name="idempotencyKey"/> prevents a double refund.</summary>
    Task<PayPalRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Vault a card, returning the vault id and safe display data. The PAN is never returned or stored.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>
    /// Return PayPal's own record of transactions across the whole [from, to] range (all pages),
    /// for lining up against eShop orders during reconciliation.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
