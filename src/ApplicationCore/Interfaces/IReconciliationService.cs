using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ReconciliationMatchState
{
    Matched = 1,
    /// <summary>PayPal recorded a transaction with no corresponding eShop order.</summary>
    PayPalOnly = 2,
    /// <summary>eShop holds a payment PayPal's report does not (yet) show.</summary>
    EshopOnly = 3
}

public sealed record ReconciliationRow(
    ReconciliationMatchState MatchState,
    string? TransactionId,
    string? TransactionStatus,
    string? TransactionEventCode,
    decimal? ProviderAmount,
    decimal? ProviderFeeAmount,
    decimal? ProviderNetAmount,
    string? ProviderCurrency,
    string? ProviderInvoiceId,
    string? ProviderReferenceId,
    int? OrderId,
    string? OrderStatus,
    decimal? OrderAmount,
    string? OrderBuyerId,
    string? OrderPaymentSummary,
    DateTimeOffset? TransactionDate);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReconciliationRow> Rows,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount,
    string CoverageNote);

public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
