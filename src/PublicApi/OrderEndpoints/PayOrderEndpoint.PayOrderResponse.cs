using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PayOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public decimal AmountCharged { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
