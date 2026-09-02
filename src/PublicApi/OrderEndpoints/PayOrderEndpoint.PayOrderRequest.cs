namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Id of a saved card (POST api/payment-methods) to pay with.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Used only when PaymentMethodId is not supplied.</summary>
    public CardDetailsDto? Card { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
