using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToModel() => new(Street, City, State, Country, ZipCode);

    /// <summary>Orders always need a shipping address; callers that only care about payment get a placeholder.</summary>
    public static Address Default() => new("n/a", "n/a", "n/a", "n/a", "n/a");
}
