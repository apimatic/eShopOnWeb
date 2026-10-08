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

    /// <summary>The order's payment state after this request (AwaitingPayment, PaymentPending, Paid, ...).</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>Paid, AlreadyPaid, Declined or Pending.</summary>
    public string Outcome { get; set; } = string.Empty;
    public PaymentAttemptDto Payment { get; set; } = new();

    /// <summary>What happened, in terms the shopper can act on.</summary>
    public string Message { get; set; } = string.Empty;
}
