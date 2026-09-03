using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateShopOrderResponse : BaseResponse
{
    public CreateShopOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateShopOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = nameof(OrderPaymentStatus.AwaitingPayment);
}
