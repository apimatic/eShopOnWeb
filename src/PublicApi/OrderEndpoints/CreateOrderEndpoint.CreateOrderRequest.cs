using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>Catalog items and quantities to order. Prices are taken from the catalog, not the caller.</summary>
    public List<OrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted (payment is the focus here).</summary>
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class OrderItemRequest
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
}
