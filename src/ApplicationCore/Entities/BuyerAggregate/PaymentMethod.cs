using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentMethod() { }
    #pragma warning restore CS8618

    public PaymentMethod(string? alias, string cardId, string last4, string? brand, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        Alias = alias;
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string? Alias { get; private set; }
    /// <summary>PayPal vault payment-token id. Never a full card number.</summary>
    public string? CardId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
}
