using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Vaults a shopper's card with PayPal and manages the saved cards.
/// Full card details pass through to PayPal only; they are never stored or logged.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
