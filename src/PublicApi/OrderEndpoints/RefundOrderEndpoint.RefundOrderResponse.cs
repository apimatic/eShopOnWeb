using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string? RefundId { get; set; }

    /// <summary>Submitted, AlreadySubmitted, ExceedsRefundable, Rejected, ProcessorTimeout, OutcomeUnknown, …</summary>
    public string Outcome { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public RefundDto? Refund { get; set; }
    public PaymentDto? Payment { get; set; }
}
