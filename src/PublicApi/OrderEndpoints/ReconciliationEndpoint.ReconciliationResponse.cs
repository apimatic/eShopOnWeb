using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<ReconciliationTransactionDto> UnmatchedTransactions { get; set; } = new();
    public IReadOnlyList<int> OrdersWithoutPayPalTransaction { get; set; } = Array.Empty<int>();
    public string Note { get; set; } = string.Empty;
}

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
}
