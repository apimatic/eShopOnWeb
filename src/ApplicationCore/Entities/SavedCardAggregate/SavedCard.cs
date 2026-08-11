using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper saved for reuse. The full card number is NEVER stored here — it lives only
/// in PayPal's vault, referenced by <see cref="VaultId"/>. We keep just enough to let the shopper
/// recognise which card it is (brand, last four, expiry) plus an optional label.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The buyer (identity name) who saved this card; used to scope access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card later. Not card data.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }
    public string Last4 { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }

    /// <summary>An optional shopper-supplied label (e.g. "Personal Visa").</summary>
    public string? Label { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultId, string last4, string? brand,
        string? expiryMonth, string? expiryYear, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultId = vaultId;
        Last4 = last4;
        Brand = brand;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Label = label;
    }
}
