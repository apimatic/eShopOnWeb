using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has vaulted at PayPal for reuse. The application stores only PayPal's vault token
/// id and a safe descriptor (brand / last four / expiry) — never the PAN, CVV or any full card detail.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>); another shopper may never see, use or delete it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? cardBrand, string? last4,
        string? expiry, string? cardHolderName, string? payPalCustomerId)
    {
        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        PayPalCustomerId = payPalCustomerId;
        PublicId = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Opaque id exposed to the shopper (the <c>paymentMethodId</c> in the API).</summary>
    public string PublicId { get; private set; }

    /// <summary>Owning shopper (JWT username). Scopes every read/use/delete.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault token id, used as <c>payment_source.card.vault_id</c> when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the card is vaulted under (for future reconciliation).</summary>
    public string? PayPalCustomerId { get; private set; }

    // Safe descriptor — enough to recognise the card, never full card details.
    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
