using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. It stores ONLY a PayPal vault-token reference plus a
/// safe descriptor (brand + last four) — never the PAN, CVV or any full card details. The card
/// itself lives in PayPal's PCI-compliant vault, addressed by <see cref="PayPalVaultId"/>.
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string cardBrand, string last4,
        string? expiry, string? label, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        Label = label;
        CreatedDate = createdAt;
    }

    /// <summary>Owner of the saved card (the authenticated shopper's identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id — the reference used to pay later. Not card data.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>Card network, e.g. "VISA". Safe to display.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits of the card. Safe to display.</summary>
    public string Last4 { get; private set; }

    /// <summary>Card expiry as "YYYY-MM" when known. Not sensitive; helps the shopper recognise the card.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional shopper-supplied nickname for the card.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
