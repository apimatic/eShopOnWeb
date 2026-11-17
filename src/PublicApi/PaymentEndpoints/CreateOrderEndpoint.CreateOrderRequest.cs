using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest
{
    public List<OrderItemInput> Items { get; set; } = new();
    public ShippingAddressInput? ShippingAddress { get; set; }
}
