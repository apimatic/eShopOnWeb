using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();
    public List<ReconciliationLocalEntry> UnmatchedLocalPayments { get; set; } = new List<ReconciliationLocalEntry>();
    public string? Note { get; set; }
}

public class ReconciliationEntry
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public class ReconciliationLocalEntry
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}
