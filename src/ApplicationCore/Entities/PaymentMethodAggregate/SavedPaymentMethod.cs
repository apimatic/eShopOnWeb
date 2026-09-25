using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) for reuse. The application stores only the PayPal vault id
/// and a safe descriptor of the card — never the card number, CVV, or any full card details.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>) for ownership scoping.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper (email/username) who saved this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault id for the saved card, used to fund a later payment.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>Card network/brand, e.g. VISA (safe to show).</summary>
    public string? Brand { get; private set; }

    /// <summary>Last digits of the card (safe to show).</summary>
    public string? LastDigits { get; private set; }

    /// <summary>Expiry in YYYY-MM (safe to show).</summary>
    public string? Expiry { get; private set; }

    /// <summary>Card holder name as it appeared on the card (safe to show).</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? brand, string? lastDigits,
        string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>A shopper-recognisable description, e.g. "VISA ****1111". Never full details.</summary>
    public string Descriptor =>
        $"{(string.IsNullOrEmpty(Brand) ? "Card" : Brand)} ****{(string.IsNullOrEmpty(LastDigits) ? "????" : LastDigits)}";
}
