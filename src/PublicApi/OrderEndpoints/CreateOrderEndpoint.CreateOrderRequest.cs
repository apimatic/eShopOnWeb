using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Places an order from catalog items. Quantities and item ids are supplied; prices come from the catalog.</summary>
public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address. When omitted a placeholder is used, since the payment flow is the focus.</summary>
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
