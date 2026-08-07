using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order. Must contain at least one line.</summary>
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address. When omitted, a default address is used.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }
}
