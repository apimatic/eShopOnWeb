using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record CreateOrderRequest
{
    public string BuyerId { get; init; } = "";
    public List<OrderLineItem> Items { get; init; } = new();
    public AddressDto ShipToAddress { get; init; } = new();
}

public record OrderLineItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public record AddressDto
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
    public string State { get; init; } = "";
    public string Country { get; init; } = "US";
    public string ZipCode { get; init; } = "";
}

public record CreateOrderResponse
{
    public int OrderId { get; init; }
}
