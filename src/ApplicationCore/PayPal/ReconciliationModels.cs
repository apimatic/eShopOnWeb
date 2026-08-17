using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>An eShop order as it appears in the reconciliation report.</summary>
public record ReconciliationOrder(
    int OrderId,
    string? Reference,
    string BuyerId,
    decimal OrderTotal,
    string PaymentStatus,
    string? PayPalOrderId,
    string? CaptureId,
    DateTimeOffset OrderDate);

/// <summary>An eShop order lined up against the PayPal transaction(s) that belong to it.</summary>
public record ReconciliationMatch(
    ReconciliationOrder Order,
    IReadOnlyList<PayPalTransaction> Transactions);

/// <summary>
/// The whole reconciliation over a date range: orders matched to PayPal transactions, PayPal
/// transactions with no eShop order, and eShop orders PayPal has no transaction for.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopOrderCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransaction> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationOrder> InEShopNotInPayPal);
