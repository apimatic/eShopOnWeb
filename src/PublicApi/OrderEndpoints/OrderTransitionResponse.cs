using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderTransitionResponse : BaseResponse
{
    public OrderTransitionResponse(Guid correlationId) : base(correlationId) { }
    public OrderTransitionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
