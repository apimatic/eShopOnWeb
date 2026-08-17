using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The full card is vaulted at PayPal; this app keeps only PayPal's
/// vault token plus a safe descriptor (brand + last four + expiry) so the shopper can recognise it.
/// No PAN or CVC is ever stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owning shopper. One shopper never sees, uses, or deletes another's card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id grouping this shopper's vaulted cards.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form (safe to keep; not sensitive on its own).</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string last4, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
