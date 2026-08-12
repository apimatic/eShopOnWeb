using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Optional ship-to address for a placed order. The order model requires an address; when the
/// caller supplies none, sensible placeholders are used so the existing order/order-item model is
/// reused unchanged.
/// </summary>
public class ShippingAddressInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>Everything needed to place an order through the API.</summary>
public class PlaceOrderInput
{
    public List<OrderLineInput> Items { get; set; } = new();
    public ShippingAddressInput? ShipToAddress { get; set; }
}
