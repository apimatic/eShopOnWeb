using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Card details a shopper supplies to save a card for later reuse.</summary>
public record SaveCardInput
{
    public string? CardNumber { get; init; }
    public string? Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>
/// Saves, lists and removes a shopper's cards. Each card belongs to the shopper who saved it;
/// full card details are never stored.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct);

    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card. Returns false when the caller has no such card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int savedCardId, CancellationToken ct);
}
