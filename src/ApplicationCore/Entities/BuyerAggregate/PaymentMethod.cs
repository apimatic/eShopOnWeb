using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentMethod() { }
    #pragma warning restore CS8618

    public PaymentMethod(string? alias, string cardId, string? last4, string? brand, string? expiry)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Alias = alias;
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
    }

    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault token id — never a PAN
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
}
