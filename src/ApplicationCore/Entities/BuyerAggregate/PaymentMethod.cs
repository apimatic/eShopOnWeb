using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A shopper's saved card. Only PayPal's vault id and a safe-to-display card summary are stored here -
/// full card details are never persisted by this application (they live only in PayPal's vault).
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultId, string cardBrand, string last4, string expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        VaultId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's vault payment-token id - the only thing needed to pay with this saved card.</summary>
    public string VaultId { get; private set; }
    public string CardBrand { get; private set; }
    public string Last4 { get; private set; }
    /// <summary>Card expiry in "YYYY-MM" form, as PayPal reports it.</summary>
    public string Expiry { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
