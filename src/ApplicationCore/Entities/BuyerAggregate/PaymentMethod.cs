using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card saved with PayPal's vault on behalf of a buyer. Only the PayPal vault id and the
/// display-safe details PayPal returns (brand/last-4/expiry) are stored here — the raw card
/// number is never held by this application.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string payPalVaultId, string? brand, string lastDigits, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalVaultId { get; private set; }
    public string? Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
