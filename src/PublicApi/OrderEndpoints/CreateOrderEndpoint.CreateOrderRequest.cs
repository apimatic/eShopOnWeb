using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequestDto> Items { get; set; } = new();
    public AddressRequestDto ShipToAddress { get; set; } = new();
}

public class OrderItemRequestDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequestDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
