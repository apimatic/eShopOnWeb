using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<OrderView> CreateOrderAsync(
        string buyerId, CreateOrderCommand command, CancellationToken cancellationToken = default);

    Task<OrderView> PayOrderAsync(
        string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<OrderView> FulfilOrderAsync(
        int orderId, CancellationToken cancellationToken = default);

    Task<OrderView> CancelOrderAsync(
        int orderId, CancellationToken cancellationToken = default);

    Task<RefundView> RefundOrderAsync(
        string buyerId, int orderId, RefundOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(
        string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethodView> SavePaymentMethodAsync(
        string buyerId, SaveCardCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethodView>> GetPaymentMethodsAsync(
        string buyerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
