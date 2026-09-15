using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ChangeOrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
