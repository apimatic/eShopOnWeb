using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. Belongs to the shopper who saved it (<see cref="BuyerId"/>);
/// stores only the PayPal vault token id and a safe, recognisable description of the card —
/// never full card details.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    // PayPalVaultTokenId is assigned once the vault call returns (SetVaultResult), not in the ctor.
#pragma warning disable CS8618 // Required by Entity Framework / set post-construction from the vault result
    private PaymentMethod() { }

    public PaymentMethod(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        // Per-save seed for a deterministic, run-unique PayPal-Request-Id on the vault call.
        IdempotencyKey = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
    }
#pragma warning restore CS8618

    public string BuyerId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>PayPal vault payment-token id — used as card.vault_id when paying. Not card data.</summary>
    public string PayPalVaultTokenId { get; private set; }

    /// <summary>PayPal customer id grouping this shopper's vaulted cards (may be minted by PayPal).</summary>
    public string? PayPalCustomerId { get; private set; }

    // Safe descriptors only.
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    public void SetVaultResult(string payPalVaultTokenId, string? payPalCustomerId,
        string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));
        PayPalVaultTokenId = payPalVaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
