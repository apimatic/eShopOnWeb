using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A saved card as shown to the shopper — never full card details.</summary>
public class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Manages a shopper's saved cards, backed by PayPal's card vault. Only the vault token and safe
/// display metadata are kept by this app. Every method is scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Save a card for a shopper. Returns the saved-card view (its id is the payment-method id).</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the caller's saved cards. Returns false if the card does not exist or is not
    /// the caller's. Afterwards the card no longer appears and can no longer be used to pay.
    /// </summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
