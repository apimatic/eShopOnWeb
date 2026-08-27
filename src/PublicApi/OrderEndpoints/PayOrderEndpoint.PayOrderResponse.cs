using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) {}
    public PayOrderResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}
