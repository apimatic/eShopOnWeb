using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A payment method saved (vaulted) for a buyer. Only the PayPal vault token and the
/// non-sensitive description returned by PayPal are kept - full card details are never
/// stored in this application's database.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>Owning buyer (set by EF through the Buyer aggregate navigation).</summary>
    public int BuyerId { get; private set; }

    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // actual card data must be stored in a PCI compliant system, like PayPal's vault

    /// <summary>PayPal vault (payment token) id used to charge the saved card.</summary>
    public string? VaultId { get; private set; }

    public string? Last4 { get; private set; }

    /// <summary>Card brand as reported by PayPal, e.g. VISA.</summary>
    public string? Brand { get; private set; }

    /// <summary>Card expiry as reported by PayPal, e.g. 2030-01.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedTime { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(int buyerId, string? alias, string vaultId, string? brand, string? last4, string? expiry)
    {
        BuyerId = buyerId;
        Alias = alias;
        VaultId = vaultId;
        CardId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }
}
