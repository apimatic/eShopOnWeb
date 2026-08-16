using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The actual card data lives in PayPal's PCI-compliant
/// vault — this record keeps only PayPal's vault token id plus a safe display summary. A card
/// number is never stored here.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string payPalVaultId, string? alias, string? brand, string? last4,
        int? expiryMonth, int? expiryYear)
    {
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Alias = alias;
        Brand = brand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A human-friendly label the shopper can give the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>
    /// PayPal vault token that references the securely stored card. This is what is sent to PayPal
    /// to pay with the saved card; it is not sensitive card data.
    /// </summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>Card brand as reported by PayPal (e.g. VISA), for display only.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits, for display only.</summary>
    public string? Last4 { get; private set; }

    public int? ExpiryMonth { get; private set; }
    public int? ExpiryYear { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
