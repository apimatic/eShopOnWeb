namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total. Provide either <see cref="Card"/> for a one-off
/// payment or <see cref="PaymentMethodId"/> to pay with a saved card.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

/// <summary>
/// Full card details are accepted only over this authenticated channel, are
/// passed straight to PayPal, and are never stored or logged.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
