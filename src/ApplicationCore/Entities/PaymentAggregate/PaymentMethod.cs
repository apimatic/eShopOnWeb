using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper's saved card. Only PayPal's vault token id and a safe, non-sensitive description
/// (brand + last four + expiry) are stored — the full card number is never kept by this app.
/// Belongs to the shopper who saved it; every read/use/delete is scoped by <see cref="BuyerId"/>.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string payPalCustomerId,
        string brand, string lastFourDigits, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.Now;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id; used as <c>payment_source.card.vault_id</c> to charge later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id that groups this shopper's vaulted instruments.</summary>
    public string PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in PayPal's "YYYY-MM" form.</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string Describe() => $"{Brand} ****{LastFourDigits}";
}
