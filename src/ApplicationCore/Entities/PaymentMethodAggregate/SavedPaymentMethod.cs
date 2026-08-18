using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. The raw card is vaulted at PayPal — this app stores only the
/// vault id plus a safe descriptor (brand, last four, expiry) so the shopper can recognise the card.
/// Full card details are never stored here. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Identity name of the owning shopper.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault id / token used to pay with this card. Not card data.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>Last four digits only — safe to show, never the full PAN.</summary>
    public string? LastDigits { get; private set; }

    /// <summary>Card expiry (YYYY-MM) as returned by the vault.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
