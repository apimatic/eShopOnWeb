using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationMatch(int OrderId, string PayPalOrderId, GatewayTransaction PayPalTransaction, bool AmountMismatch);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<GatewayTransaction> PayPalOnly,
    IReadOnlyList<Order> EShopOnly);

/// <summary>Lines up PayPal's own transaction record for a date range against eShop's local order/payment records.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
