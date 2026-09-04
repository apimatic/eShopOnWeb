using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Quantity);

public record ReconciliationEntry(
    string TransactionId,
    string Status,
    GatewayMoney Amount,
    DateTimeOffset InitiationDate,
    int? OrderId,
    string? OrderStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalTransactions,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<ReconciliationEntry> UnmatchedOrders);

public record RefundOutcome(
    PaymentRefund Refund,
    decimal RefundedAmount,
    decimal RefundableAmount);

/// <summary>
/// Order placement and the payment lifecycle that follows it: authorize at checkout,
/// capture at fulfilment, release on cancellation, refund on return.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderItemRequest> items, CancellationToken ct);
    Task<Order> PayOrderAsync(string buyerId, int orderId, GatewayCard? card, int? paymentMethodId, CancellationToken ct);
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct);
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct);
    Task<RefundOutcome> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Saved cards: save once, reuse without re-entering card details.</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, GatewayCard card, CancellationToken ct);
    Task<IReadOnlyList<PaymentMethod>> ListForBuyerAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
