using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReportDto> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReportDto
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int PayPalTransactionCount { get; init; }
    public int MatchedCount { get; init; }
    public IReadOnlyList<ReconciliationMatchDto> Matches { get; init; } = Array.Empty<ReconciliationMatchDto>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<UnmatchedOrderPaymentDto> EShopOnly { get; init; } = Array.Empty<UnmatchedOrderPaymentDto>();
}

public sealed class ReconciliationMatchDto
{
    public int OrderId { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public PayPalReportedTransaction PayPalTransaction { get; init; } = null!;
}

public sealed class UnmatchedOrderPaymentDto
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}
