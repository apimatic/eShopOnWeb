using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>Catalog items and quantities to order. Must contain at least one item.</summary>
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address. A default is used when omitted.</summary>
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItem
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
