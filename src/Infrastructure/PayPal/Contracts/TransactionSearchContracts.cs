using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- Transaction Search v1: reporting/transactions ---

internal sealed class SearchResponseDto
{
    public List<TransactionDetailDto>? TransactionDetails { get; set; }
    public int Page { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public List<LinkDto>? Links { get; set; }
}

internal sealed class TransactionDetailDto
{
    public TransactionInfoDto? TransactionInfo { get; set; }
}

internal sealed class TransactionInfoDto
{
    public string? TransactionId { get; set; }
    public string? TransactionEventCode { get; set; }
    public string? TransactionInitiationDate { get; set; }
    public string? TransactionUpdatedDate { get; set; }
    public MoneyDto? TransactionAmount { get; set; }
    public MoneyDto? FeeAmount { get; set; }
    public string? TransactionStatus { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? PaypalReferenceId { get; set; }
}
