using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string cardId, string last4, string? brand, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));

        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        Alias = alias ?? BuildAlias(brand, last4);
    }

    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault payment-token id; never a PAN
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }

    public static string BuildAlias(string? brand, string? last4)
    {
        var network = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        return string.IsNullOrWhiteSpace(last4) ? network : $"{network} ending {last4}";
    }
}
