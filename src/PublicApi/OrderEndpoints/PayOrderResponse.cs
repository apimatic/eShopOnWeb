using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
