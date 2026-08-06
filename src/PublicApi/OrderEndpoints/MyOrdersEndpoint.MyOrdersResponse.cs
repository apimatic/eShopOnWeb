using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
