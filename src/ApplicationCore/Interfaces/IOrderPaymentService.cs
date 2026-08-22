using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderResult(Order Order);

public record RefundOrderResult(Order Order, OrderRefund Refund);

public record ReconciliationRow(
    string Match,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    string? Status,
    decimal? EshopAmount,
    decimal? PayPalAmount,
    string? Currency,
    string? Note);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? PayPalLastRefreshed,
    IReadOnlyList<ReconciliationRow> Rows);

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Order> AuthorizePaymentAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<RefundOrderResult> RefundOrderAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
