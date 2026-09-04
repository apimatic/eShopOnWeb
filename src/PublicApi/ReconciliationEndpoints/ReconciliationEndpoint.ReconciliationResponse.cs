using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public int TotalPayPalTransactions { get; set; }
    public List<PayPalTransactionDto> PayPalTransactions { get; set; } = new();
    public List<UnmatchedOrderDto> OrdersWithoutPayPalRecord { get; set; } = new();
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string EventCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset InitiationDate { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public string? CustomField { get; set; }
    public string? InvoiceId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? PayPalReferenceIdType { get; set; }
    public bool Matched { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class UnmatchedOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}