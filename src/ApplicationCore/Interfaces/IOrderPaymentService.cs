using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record OrderLineItem(int CatalogItemId, int Quantity);

public sealed record OrderPaymentMethod(PayPalCardDetails? Card, Guid? PaymentMethodId);

public sealed record ReconciliationRow(
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? Date,
    decimal? GrossAmount,
    decimal? FeeAmount,
    decimal? NetAmount,
    string? Currency,
    string? PayerEmail,
    int? OrderId,
    string? OrderStatus,
    decimal? OrderTotal,
    string Relation);

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> items, Address shipToAddress, CancellationToken ct);

    Task<Order> PayAsync(string buyerId, int orderId, OrderPaymentMethod payment, CancellationToken ct);

    Task<Order> FulfilAsync(int orderId, CancellationToken ct);

    Task<Order> CancelAsync(int orderId, CancellationToken ct);

    Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId, decimal amount, string idempotencyKey, CancellationToken ct);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, PayPalCardDetails card, CancellationToken ct);

    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct);

    Task DeletePaymentMethodAsync(string buyerId, Guid paymentMethodId, CancellationToken ct);

    Task<IReadOnlyList<ReconciliationRow>> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}