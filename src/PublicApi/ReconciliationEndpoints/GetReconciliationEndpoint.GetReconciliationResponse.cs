using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<UnmatchedLocalPaymentDto> UnmatchedLocalPayments { get; set; } = new List<UnmatchedLocalPaymentDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? Time { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? Status { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }

    /// <summary>"Matched" when lined up with a local payment, "OnlyInPayPal" otherwise.</summary>
    public string MatchState { get; set; } = string.Empty;
}

public class UnmatchedLocalPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Always "OnlyInEShop" — PayPal reported no matching transaction in the range.</summary>
    public string MatchState { get; set; } = "OnlyInEShop";
}
