using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>JSON body for POST api/orders.</summary>
public class PlaceOrderRequestBody
{
    public List<PlaceOrderItemDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string State { get; set; } = default!;
    public string Country { get; set; } = default!;
    public string ZipCode { get; set; } = default!;
}

public class PlaceOrderRequest : BaseRequest
{
    public PlaceOrderRequest(string buyerId, List<PlaceOrderItemDto> items, ShipToAddressDto? shipToAddress)
    {
        BuyerId = buyerId;
        Items = items;
        ShipToAddress = shipToAddress;
    }

    public string BuyerId { get; }
    public List<PlaceOrderItemDto> Items { get; }
    public ShipToAddressDto? ShipToAddress { get; }
}
