using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application database holds only a reference to the
/// card vaulted at PayPal (<see cref="VaultId"/>) plus safe descriptive fields — never the full
/// card number, which lives exclusively inside PayPal's vault.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string payPalCustomerId, string brand, string last4, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the saved card. Only this shopper may see, use, or delete it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal payment-method (vault) token id used to charge the card.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id the card is vaulted under; reused to link a shopper's cards.</summary>
    public string PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }

    public string Last4 { get; private set; }

    /// <summary>Expiry in "YYYY-MM" form, as reported by PayPal.</summary>
    public string Expiry { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Safe, human-readable label, e.g. "VISA ****1111".</summary>
    public string Descriptor => $"{Brand} ****{Last4}";
}
