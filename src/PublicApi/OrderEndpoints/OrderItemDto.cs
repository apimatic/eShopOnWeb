namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A catalog item and quantity requested when placing an order.</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>An order line as placed, with the unit price snapshotted from the catalog.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>Optional shipping address for an order. Not part of the bill.</summary>
public class OrderAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
