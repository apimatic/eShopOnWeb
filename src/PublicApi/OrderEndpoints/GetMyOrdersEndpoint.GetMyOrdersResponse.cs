using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
