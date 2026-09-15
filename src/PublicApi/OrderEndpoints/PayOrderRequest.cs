namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public PayOrderCardRequest? Card { get; set; }
}

public class PayOrderCardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public PayOrderBillingAddressRequest? BillingAddress { get; set; }
}

public class PayOrderBillingAddressRequest
{
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
