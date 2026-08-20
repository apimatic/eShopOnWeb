using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class CreateOrderLineRequest
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

    public Address ToAddress() =>
        new(
            string.IsNullOrWhiteSpace(Street) ? "123 Main Street" : Street,
            string.IsNullOrWhiteSpace(City) ? "Seattle" : City,
            string.IsNullOrWhiteSpace(State) ? "WA" : State,
            string.IsNullOrWhiteSpace(Country) ? "USA" : Country,
            string.IsNullOrWhiteSpace(ZipCode) ? "98101" : ZipCode);
}
