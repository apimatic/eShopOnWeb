using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderWithRefunds> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<NewOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default);

    Task<OrderWithRefunds> AuthorizeOrderAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? savedPaymentMethodId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OrderWithRefunds> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderWithRefunds> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundOrderAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithRefunds>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(
        string ownerId,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string ownerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
