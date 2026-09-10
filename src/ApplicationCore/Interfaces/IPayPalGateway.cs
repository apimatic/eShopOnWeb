using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary over PayPal. The only type that talks to the PayPal SDK; it maps PayPal's models onto the
/// domain DTOs in <see cref="Payments"/> and translates PayPal/transport failures into
/// <see cref="Exceptions.PaymentGatewayException"/> / <see cref="Exceptions.PaymentChallengeException"/>.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> for an order. Supply either raw <paramref name="card"/>
    /// details for a one-off payment, or a <paramref name="vaultId"/> to charge a saved card. The held
    /// amount equals the order total to the cent. <paramref name="idempotencyKey"/> makes a repeat
    /// harmless at PayPal.
    /// </summary>
    Task<AuthorizationOutcome> AuthorizeAsync(string invoiceId, decimal amount, string currency,
        string description, CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken ct);

    /// <summary>Captures (takes) the authorized payment. Returns PayPal's captured amount, fee and net.</summary>
    Task<CaptureOutcome> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken ct);

    /// <summary>Re-authorizes a stale hold so fulfilment can proceed.</summary>
    Task<ReauthorizeOutcome> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Voids (releases) an authorization before capture; no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full (null amount) or in part.</summary>
    Task<RefundOutcome> RefundAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Saves a card in the PayPal vault and returns a safe description of it. Vaults via a nominal
    /// authorization (in <paramref name="currency"/>) that is released immediately, so the card is stored
    /// without any money being taken.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(string? existingCustomerId, string merchantCustomerId,
        CardDetails card, string currency, CancellationToken ct);

    /// <summary>Removes a saved card from the PayPal vault.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (every page), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        string? currency, CancellationToken ct);
}
