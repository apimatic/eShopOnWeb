using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    /// <summary>The catalog items and quantities to place on the order.</summary>
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional ship-to address. A placeholder is used when omitted, since this flow is about billing.</summary>
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
