using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

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

    public Address ToAddress() => new(
        string.IsNullOrWhiteSpace(Street) ? "N/A" : Street,
        string.IsNullOrWhiteSpace(City) ? "N/A" : City,
        string.IsNullOrWhiteSpace(State) ? "N/A" : State,
        string.IsNullOrWhiteSpace(Country) ? "N/A" : Country,
        string.IsNullOrWhiteSpace(ZipCode) ? "00000" : ZipCode);
}
