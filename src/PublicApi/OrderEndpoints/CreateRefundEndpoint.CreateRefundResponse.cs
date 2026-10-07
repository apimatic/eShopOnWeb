using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundResponse : BaseResponse
{
    public CreateRefundResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateRefundResponse()
    {
    }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal RemainingRefundable { get; set; }
    public string Message { get; set; } = string.Empty;
}
