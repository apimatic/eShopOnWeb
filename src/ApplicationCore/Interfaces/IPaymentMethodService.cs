using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    /// <summary>Vaults a card in PayPal and saves a safe reference to it for the given buyer.</summary>
    Task<SavedCardInfo> SaveCardAsync(
        string buyerId, PaymentCard card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Lists the buyer's saved cards (safe descriptors only).</summary>
    Task<IReadOnlyList<SavedCardInfo>> ListCardsAsync(
        string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one of the buyer's saved cards from both the application and PayPal's vault. Returns
    /// false if the buyer has no such card. After deletion the card can no longer be used to pay.
    /// </summary>
    Task<bool> DeleteCardAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

/// <summary>Safe, display-only description of a saved card. Never contains full card details.</summary>
public class SavedCardInfo
{
    public SavedCardInfo(int paymentMethodId, string alias, string brand, string last4, string expiry)
    {
        PaymentMethodId = paymentMethodId;
        Alias = alias;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    public int PaymentMethodId { get; }
    public string Alias { get; }
    public string Brand { get; }
    public string Last4 { get; }
    public string Expiry { get; }
}
