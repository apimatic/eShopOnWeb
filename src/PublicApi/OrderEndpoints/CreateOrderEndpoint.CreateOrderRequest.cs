using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new List<OrderLineDto>();
    public AddressDto? ShipToAddress { get; set; }
}
