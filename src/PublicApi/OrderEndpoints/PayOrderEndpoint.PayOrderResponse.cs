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

    /// <summary>Paid, AlreadyPaid, Refused, Pending, ActionRequired, Rejected, InProgress, NotFound, NotPayable, Invalid, ProviderUnavailable, ProviderDidNotRespond.</summary>
    public string Status { get; set; } = "";

    /// <summary>What happened, in terms the shopper can act on.</summary>
    public string Message { get; set; } = "";

    /// <summary>The order's payment state after this call.</summary>
    public string? PaymentStatus { get; set; }

    public string? PspReference { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? RefusalReason { get; set; }
}
