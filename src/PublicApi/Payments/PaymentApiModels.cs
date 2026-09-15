namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}
public sealed class PlaceOrderItemRequest { public int CatalogItemId { get; set; } public int Quantity { get; set; } }
public sealed class ShippingAddressRequest { public string Street { get; set; } = "Not supplied"; public string City { get; set; } = "Not supplied"; public string State { get; set; } = string.Empty; public string Country { get; set; } = "Not supplied"; public string ZipCode { get; set; } = "Not supplied"; }
public sealed class PayOrderRequest { public CardRequest? Card { get; set; } public int? PaymentMethodId { get; set; } }
public sealed class SavePaymentMethodRequest { public CardRequest Card { get; set; } = new(); }
public sealed class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardAddressRequest BillingAddress { get; set; } = new();
}
public sealed class CardAddressRequest { public string AddressLine1 { get; set; } = string.Empty; public string? AddressLine2 { get; set; } public string AdminArea2 { get; set; } = string.Empty; public string? AdminArea1 { get; set; } public string PostalCode { get; set; } = string.Empty; public string CountryCode { get; set; } = string.Empty; }
public sealed class RefundOrderRequest { public decimal? Amount { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
    public int StatusCode { get; }
}
