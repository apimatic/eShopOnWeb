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

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public RefundDto Refund { get; set; } = new();

    /// <summary>The order's payment state after this request (Paid, PartiallyRefunded, Refunded).</summary>
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal AmountRefundable { get; set; }
}
