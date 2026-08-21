using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? FeeAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public int? OrderId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public class ReconciliationApiResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<int> EshopOrdersMissingFromPayPal { get; set; } = new();
}
