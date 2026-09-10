using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A saved card described safely enough for a shopper to recognise it — never full details.</summary>
public record SavedCardView(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? CardholderName);

public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and returns its safe description.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's saved cards so it can no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
