namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details for this payment. Never stored.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of a saved card (POST /api/payment-methods) to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
}

public class CardBillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
