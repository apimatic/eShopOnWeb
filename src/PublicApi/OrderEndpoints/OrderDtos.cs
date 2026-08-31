using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for the order. Defaulted when not supplied.</summary>
public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Places an order from catalog items. The caller's identity comes from the token, not this body.</summary>
public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public List<CreateOrderItemResponse> Items { get; set; } = new();
}
