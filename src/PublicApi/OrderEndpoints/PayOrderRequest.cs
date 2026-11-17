namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }

    /// <summary>Optional CVC when paying with a saved card.</summary>
    public string? SecurityCode { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardAddressRequest? BillingAddress { get; set; }
}

public class CardAddressRequest
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
