using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Saved-card management, scoped to the signed-in shopper.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and return its safe descriptor. Returns the new id.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove a saved card; afterwards it is neither listed nor usable to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}

/// <summary>A saved card, described safely enough to recognise but never with full card details.</summary>
public sealed record SavedCardView(
    int Id,
    string Brand,
    string LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt,
    string Display);
