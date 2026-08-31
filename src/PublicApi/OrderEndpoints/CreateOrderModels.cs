using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record CreateOrderItem(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for the order.</summary>
public record ShippingAddressDto(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// Places an order from catalog items. The caller's identity comes from the token, so no buyer is
/// carried in the body. Prices come from the catalog, never from the caller.
/// </summary>
public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

/// <summary>Result of placing an order. <c>orderId</c> is returned as a top-level field.</summary>
public class CreateOrderResponse
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public required decimal Total { get; init; }
    public required string Currency { get; init; }
    public IReadOnlyList<CreateOrderResponseItem> Items { get; init; } = Array.Empty<CreateOrderResponseItem>();
}

public record CreateOrderResponseItem(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);
