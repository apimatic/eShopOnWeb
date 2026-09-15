using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved (vaulted with PayPal) for reuse on later orders. The application stores only
/// the PayPal vault token and a safe descriptor of the card — never the full card number or CVV.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owning shopper (username/email). A card belongs to the shopper who saved it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault id used later as payment_source.card.vault_id when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id the vault token is associated with.</summary>
    public string PayPalCustomerId { get; private set; }

    // Safe descriptor only — enough for the shopper to recognise the card.
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string ExpiryYearMonth { get; private set; }
    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string payPalCustomerId,
        string brand, string lastDigits, string expiryYearMonth, string? cardHolderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = string.IsNullOrEmpty(brand) ? "UNKNOWN" : brand;
        LastDigits = lastDigits ?? string.Empty;
        ExpiryYearMonth = expiryYearMonth ?? string.Empty;
        CardHolderName = cardHolderName;
    }
}
