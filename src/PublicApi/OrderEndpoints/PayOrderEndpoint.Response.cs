using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string AuthorizationId { get; set; } = "";
    public string Status { get; set; } = "";
}
