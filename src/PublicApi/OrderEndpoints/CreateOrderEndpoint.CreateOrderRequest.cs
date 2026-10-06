using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemDto>? Items { get; set; }

    /// <summary>Optional, at most 200 characters. Stored only on the Square order.</summary>
    public string? GiftMessage { get; set; }

    /// <summary>Optional. Without it the order is collected at the merchant's Square location.</summary>
    public OrderAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
