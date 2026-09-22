using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Every action is scoped to the caller: one shopper never sees,
/// uses, or deletes another's card. Full card details are never stored — only the PayPal token and a safe
/// descriptor.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the caller and return a safe description of it.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Delete the caller's saved card; afterwards it no longer appears and cannot be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

public record SavedCardView
{
    public required int PaymentMethodId { get; init; }
    public string? CardBrand { get; init; }
    public string? LastFourDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
