using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted with PayPal) for reuse on later orders. The application's
/// own database never stores full card details — only PayPal's vault id and a safe description
/// (brand, last four, expiry) so the shopper can recognise which card it is.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper who owns this saved card (their identity, from the auth token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault id / payment-token id used to charge the card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network brand, e.g. VISA (safe to show).</summary>
    public string Brand { get; private set; }

    /// <summary>Last four digits (safe to show).</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry month, two digits.</summary>
    public string ExpiryMonth { get; private set; }

    /// <summary>Expiry year, four digits.</summary>
    public string ExpiryYear { get; private set; }

    /// <summary>Optional shopper-supplied label.</summary>
    public string? Alias { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultId, string brand, string last4,
        string expiryMonth, string expiryYear, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand ?? string.Empty;
        Last4 = last4 ?? string.Empty;
        ExpiryMonth = expiryMonth ?? string.Empty;
        ExpiryYear = expiryYear ?? string.Empty;
        Alias = alias;
    }

    /// <summary>Safe, human-recognisable description of the card.</summary>
    public string Describe() =>
        string.IsNullOrEmpty(Brand) ? $"****{Last4}" : $"{Brand} ****{Last4}";
}
