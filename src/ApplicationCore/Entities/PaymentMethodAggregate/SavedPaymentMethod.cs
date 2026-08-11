using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse (Flow 2). The card itself lives in PayPal's vault;
/// this row keeps only the vault token, the owning shopper and a safe description of the card
/// (brand / last four digits / expiry) so the shopper can recognise it. No full card details
/// are ever stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string paypalVaultId,
        string paypalCustomerId,
        string cardBrand,
        string cardLast4,
        string cardExpiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(paypalVaultId, nameof(paypalVaultId));
        Guard.Against.NullOrEmpty(paypalCustomerId, nameof(paypalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = paypalVaultId;
        PayPalCustomerId = paypalCustomerId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this card (their identity / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer this card is vaulted under.</summary>
    public string PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }
    public string CardLast4 { get; private set; }

    /// <summary>Card expiry in PayPal's YYYY-MM form.</summary>
    public string CardExpiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
