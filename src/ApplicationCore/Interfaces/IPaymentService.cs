using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderPaymentView> CreateOrderAsync(string buyerId, IReadOnlyList<CreateOrderItem> items,
        ShippingAddress address, CancellationToken cancellationToken);
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken);
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderPaymentView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<SavedCardView> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedCardView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
