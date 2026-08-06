using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    /// <summary>Catalog items and quantities to order. Prices come from the catalog (USD).</summary>
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address. A placeholder is used when omitted.</summary>
    public AddressDto? ShipToAddress { get; set; }
}
