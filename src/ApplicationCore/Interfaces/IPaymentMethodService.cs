using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns its safe descriptor + new id.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken ct = default);

    /// <summary>Lists the caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes the caller's saved card so it can no longer appear or be used to pay.</summary>
    Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken ct = default);
}
