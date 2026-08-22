using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, string fromRaw, string toRaw, CancellationToken cancellationToken);
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public string? LastRefreshedDatetime { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<PayPalTransactionRow> PayPalOnly { get; init; }
    public required IReadOnlyList<EshopPaymentRow> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required int OrderId { get; init; }
    public required PayPalTransactionRow PayPal { get; init; }
}

public sealed class PayPalTransactionRow
{
    public string? TransactionId { get; init; }
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? TransactionStatus { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public sealed class EshopPaymentRow
{
    public required int OrderId { get; init; }
    public string? BuyerId { get; init; }
    public string? Status { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public decimal Total { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}
