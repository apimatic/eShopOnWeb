using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's vaulted (saved) cards. All operations are scoped to the owner.</summary>
public interface ISavedCardService
{
    /// <summary>Vault a card at PayPal and store safe display details against the shopper.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    /// <summary>The caller's own saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Remove one of the caller's saved cards (deletes the PayPal vault token too). After this the
    /// card no longer appears in the list and can no longer be used to pay.
    /// </summary>
    Task DeleteAsync(string buyerId, int savedCardId, CancellationToken ct = default);
}
