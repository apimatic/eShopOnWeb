using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A shopper's saved card. The raw card number is never stored here: <see cref="VaultId"/> is
/// PayPal's vault token id, and the remaining fields are the safe-to-display descriptors PayPal
/// returns alongside it (brand, last 4 digits, expiry) so the shopper can recognise the card.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(int buyerId, string vaultId, string? brand, string? lastDigits, string? expiry)
    {
        Guard.Against.NegativeOrZero(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
    }

    public int BuyerId { get; private set; }

    /// <summary>PayPal's vault token id — the only value used to pay with this saved card.</summary>
    public string VaultId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
}
