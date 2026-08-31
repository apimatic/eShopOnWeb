using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<OrderItemRequestDto> Items { get; set; } = new();

    /// <summary>Optional ship-to address; a placeholder is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }
}
