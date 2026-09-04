using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

