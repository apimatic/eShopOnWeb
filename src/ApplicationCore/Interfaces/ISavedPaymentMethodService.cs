using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards. Everything is scoped to the calling shopper.</summary>
public interface ISavedPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns its safe descriptor. Returns the new payment-method id.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>Lists the caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card so it no longer appears and can no longer pay. No-op-safe if already gone.</summary>
    Task DeleteAsync(string buyerId, Guid paymentMethodId, CancellationToken ct);
}
