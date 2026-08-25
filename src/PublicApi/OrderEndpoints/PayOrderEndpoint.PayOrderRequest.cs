namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the route - ignore any value supplied by the client.</summary>
    public int OrderId { get; set; }

    /// <summary>Set by the endpoint from the caller's JWT - ignore any value supplied by the client.</summary>
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Provide this to pay with a one-off card, or <see cref="SavedPaymentMethodId"/> to pay with a saved card - not both.</summary>
    public CardPaymentRequest? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>"YYYY-MM"</summary>
    public string ExpiryYearMonth { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
