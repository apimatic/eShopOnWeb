using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Save, list and delete a shopper's vaulted cards. Every operation is scoped to the caller: one shopper
/// never sees, uses or deletes another's. Full card details are never stored.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct);

    Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card. Returns false when the caller has no such card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

public record SaveCardInput(string Number, string Expiry, string SecurityCode, string? Name,
    string? CountryCode, string? AddressLine1, string? AddressLine2, string? AdminArea1, string? AdminArea2,
    string? PostalCode);

public record SavedCardView(int PaymentMethodId, string? Brand, string? LastDigits, string? Expiry,
    string? CardholderName);
