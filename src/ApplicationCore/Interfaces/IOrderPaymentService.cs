using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken cancellationToken);

    Task<Order> PayAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public record PayOrderRequest(int? PaymentMethodId, CardPaymentInput? Card);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<PayPalReportedTransaction> PayPalOnly,
    IReadOnlyList<ReconciliationOrderRow> EShopOnly);

public record ReconciliationMatch(int OrderId, string? PayPalTransactionId, string MatchReason);

public record ReconciliationOrderRow(
    int OrderId,
    OrderStatus Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Total);
