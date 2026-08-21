using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? BuyerId { get; set; }

    public Address ToAddress()
        => new(
            string.IsNullOrWhiteSpace(Street) ? "123 Main St" : Street,
            string.IsNullOrWhiteSpace(City) ? "Redmond" : City,
            string.IsNullOrWhiteSpace(State) ? "WA" : State,
            string.IsNullOrWhiteSpace(Country) ? "USA" : Country,
            string.IsNullOrWhiteSpace(ZipCode) ? "98052" : ZipCode);
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
