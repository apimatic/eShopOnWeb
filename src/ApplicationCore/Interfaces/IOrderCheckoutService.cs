using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> lines,
        Address shippingAddress,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<EShopUnmatchedPayment> EShopOnly { get; init; } = Array.Empty<EShopUnmatchedPayment>();
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? MatchReason { get; init; }
}

public sealed class EShopUnmatchedPayment
{
    public int OrderId { get; init; }
    public string PaymentStatus { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
}
