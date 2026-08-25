using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The raw card number is never stored here — only the
/// PayPal vault id (used to pay with the card again) and a display-safe descriptor (brand, last
/// 4 digits, expiry) so the shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string vaultId, string cardBrand, string lastDigits, string expiry, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated payment-token id used as the payment source when paying with this saved card.</summary>
    public string VaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
