using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card the shopper vaulted with the payment processor for reuse. Only safe display
/// data is stored here — never the full card number or security code; the processor's
/// vault token id is the handle used to charge the card again.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultTokenId, string? brand, string? lastDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id (payment-tokens v3 resource id).</summary>
    public string VaultTokenId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM format, as reported by the vault.</summary>
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
