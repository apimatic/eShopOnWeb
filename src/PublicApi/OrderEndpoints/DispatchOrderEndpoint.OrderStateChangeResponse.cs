using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStateChangeResponse : BaseResponse
{
    public OrderStateChangeResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
