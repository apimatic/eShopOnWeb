using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault — this record only
/// keeps PayPal's opaque vault token plus a safe descriptor (brand, last four, expiry) so the shopper
/// can recognise the card. No full card number, CVC or other cardholder data is ever stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Identity of the shopper who owns this card (the authenticated user name from the token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card again without re-entering it.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>Card network, e.g. VISA — safe to display.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits — safe to display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Expiry in PayPal's YYYY-MM form — safe to display.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional cardholder name / label the shopper chose.</summary>
    public string? CardholderName { get; private set; }

    public System.DateTimeOffset CreatedDate { get; private set; } = System.DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? brand, string? last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
