using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    /// <summary>Catalog items and quantities to order.</summary>
    public List<PlaceOrderLine> Items { get; set; } = new();

    /// <summary>Optional shipping address; sensible defaults are used when omitted (no storefront UI collects it).</summary>
    public ShipToAddressDto? ShipToAddress { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }

    /// <summary>The created order together with the notifications raised for it.</summary>
    public OrderDto Order { get; set; } = new();
}
