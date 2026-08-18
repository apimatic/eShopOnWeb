using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved cards for a shopper. A card is vaulted at PayPal; this app keeps only the vault id and a
/// safe descriptor. A saved card belongs to the shopper who saved it — one shopper never sees, uses
/// or deletes another's.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and return its safe descriptor.</summary>
    Task<SavedPaymentMethodView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethodView>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the shopper's saved card (also deletes it from the PayPal vault). Returns false when the
    /// card does not exist for this shopper.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

/// <summary>Safe, shopper-facing description of a saved card — never full card details.</summary>
public record SavedPaymentMethodView(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt);
