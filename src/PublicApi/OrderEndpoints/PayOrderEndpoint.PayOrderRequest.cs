namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Route-bound.</summary>
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods) to pay with.</summary>
    public int? PaymentMethodId { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
