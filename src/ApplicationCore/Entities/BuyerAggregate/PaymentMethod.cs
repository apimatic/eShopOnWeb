using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The raw card is never stored here — only PayPal's vault token id
/// and a safe, non-sensitive description (brand, last four digits, expiry) that lets the shopper recognise
/// which card it is. Charging the card later is done by handing the <see cref="VaultId"/> back to PayPal.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>An optional shopper-facing label for the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>The PayPal vault token id used to charge this card. Not the card number.</summary>
    public string VaultId { get; private set; }

    /// <summary>The card network/brand, e.g. VISA. Safe to show.</summary>
    public string? Brand { get; private set; }

    /// <summary>The last digits of the card. Safe to show.</summary>
    public string? Last4 { get; private set; }

    /// <summary>The card expiry as YYYY-MM. Safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>The cardholder name as stored at the vault. Safe to show.</summary>
    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultId, string? brand, string? last4, string? expiry, string? cardHolderName, string? alias)
    {
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        Alias = alias;
    }
}
