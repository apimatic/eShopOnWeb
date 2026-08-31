using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>One transaction from the gateway's own reporting (its books, not ours).</summary>
public class GatewayTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    /// <summary>Related pre-existing transaction or order id on the gateway side.</summary>
    public string? ReferenceId { get; set; }
    /// <summary>Reference id type, e.g. ODR (order) or TXN (transaction).</summary>
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? Status { get; set; }
}
