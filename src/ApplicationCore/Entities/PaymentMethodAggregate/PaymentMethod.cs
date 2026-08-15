using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card the shopper saved for reuse. The card itself lives in PayPal's vault; this record keeps
/// only the vault token id plus a safe descriptor (brand, last four, expiry) so the shopper can
/// recognise it. No PAN or CVV is ever stored here.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultTokenId, string brand, string lastDigits,
        string? expiry, string? cardholderName)
    {
        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner; a saved card belongs to the shopper who saved it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id, used as the payment source when reused.</summary>
    public string VaultTokenId { get; private set; }

    public string Brand { get; private set; }

    /// <summary>Last four digits only.</summary>
    public string LastDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM, when PayPal returned it.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Shopper-facing label, e.g. "Visa ****1111".</summary>
    public string Display => $"{Brand} ****{LastDigits}";
}
