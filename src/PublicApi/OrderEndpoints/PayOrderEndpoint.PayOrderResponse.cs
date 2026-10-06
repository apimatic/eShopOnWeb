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

    /// <summary>Paid, AlreadyPaid, Refused, CardDetailsRejected, AuthenticationNotSupported, Pending, InProgress, ProviderUnavailable, AmountNotChargeable.</summary>
    public string Outcome { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>What happened, in terms the shopper can act on.</summary>
    public string Message { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public int? AttemptNumber { get; set; }
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public string? RefusalReasonCode { get; set; }
}
