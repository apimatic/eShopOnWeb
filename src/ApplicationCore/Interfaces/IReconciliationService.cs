using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationRow> PayPalTransactions { get; init; }
    public required IReadOnlyList<UnmatchedOrderRow> EshopOrdersWithoutPayPalMatch { get; init; }
}

public sealed class ReconciliationRow
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? PaypalReferenceIdType { get; init; }
    public string? TransactionEventCode { get; init; }
    public DateTimeOffset? TransactionInitiationDate { get; init; }
    public decimal? TransactionAmount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? TransactionStatus { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public int? MatchedOrderId { get; init; }
    public string MatchStatus { get; init; } = "UnmatchedInEshop";
}

public sealed class UnmatchedOrderRow
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public decimal Total { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}
