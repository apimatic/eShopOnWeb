using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public OrderAddressInput ToInput() => new(Street, City, State, Country, ZipCode);
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public ShippingAddressRequest? BillingAddress { get; set; }

    public CardPaymentInput ToInput() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToInput());
}
