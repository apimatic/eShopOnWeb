using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse
{
    public string? From { get; set; }
    public string? To { get; set; }
    public int TotalTransactions { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
}

public class ReconciliationRow
{
    public string? TransactionId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? Status { get; set; }
    public string? InitiationDate { get; set; }
    public int? OrderId { get; set; }
    public string? BuyerId { get; set; }
    public string? OrderStatus { get; set; }
}
