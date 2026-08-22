using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address? ToAddress()
    {
        if (string.IsNullOrWhiteSpace(Street) &&
            string.IsNullOrWhiteSpace(City) &&
            string.IsNullOrWhiteSpace(State) &&
            string.IsNullOrWhiteSpace(Country) &&
            string.IsNullOrWhiteSpace(ZipCode))
        {
            return null;
        }

        return new Address(
            Street ?? "123 Main Street",
            City ?? "Seattle",
            State ?? "WA",
            Country ?? "USA",
            ZipCode ?? "98101");
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}
