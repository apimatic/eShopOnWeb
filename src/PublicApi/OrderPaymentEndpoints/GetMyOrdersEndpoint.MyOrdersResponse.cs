using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}
