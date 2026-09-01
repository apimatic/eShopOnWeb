using System;
using Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>True when the hold had gone stale and was renewed before the capture.</summary>
    public bool AuthorizationRenewed { get; set; }
    public PaymentDto? Payment { get; set; }
}
