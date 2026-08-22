using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A shopper's vaulted card. Stores only PayPal's vault token and display metadata — never the PAN.
/// </summary>
public class ShopperPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private ShopperPaymentMethod() { }
#pragma warning restore CS8618

    public ShopperPaymentMethod(
        string buyerId,
        string payPalCustomerId,
        string payPalVaultId,
        string lastDigits,
        string brand,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalVaultId = payPalVaultId;
        LastDigits = lastDigits;
        Brand = brand ?? string.Empty;
        Expiry = expiry ?? string.Empty;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string LastDigits { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
