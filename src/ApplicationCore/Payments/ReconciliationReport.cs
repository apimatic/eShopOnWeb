using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Lines PayPal's own record of transactions up against eShop orders for a date range.
/// Entries with a null MatchedOrderId are payments PayPal knows about and eShop doesn't;
/// UnmatchedEShopOrders are payments eShop knows about and PayPal's report doesn't.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<UnmatchedEShopOrder> UnmatchedEShopOrders);

public sealed record ReconciliationEntry(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    DateTimeOffset? Time,
    int? MatchedOrderId,
    string MatchType);

public sealed record UnmatchedEShopOrder(
    int OrderId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds);
