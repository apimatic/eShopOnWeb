namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int? PaymentMethodId { get; set; }
    public CardDetails? Card { get; set; }
}

public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? CountryCode { get; set; }
    public string? PostalCode { get; set; }
}
