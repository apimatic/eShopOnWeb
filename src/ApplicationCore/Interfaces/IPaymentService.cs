using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderSummary> CreateOrderAsync(string buyerId, CreateOrderCommand command, CancellationToken cancellationToken = default);

    Task<OrderSummary> AuthorizeOrderAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default);

    Task<OrderSummary> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderSummary> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundSummary> RefundOrderAsync(string buyerId, int orderId, RefundCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<SavedCardSummary> SavePaymentMethodAsync(string buyerId, SavePaymentMethodCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardSummary>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
