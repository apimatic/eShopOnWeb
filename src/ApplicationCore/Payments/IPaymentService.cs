using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentService
{
    Task<OrderView> CreateOrderAsync(string buyerId, CreateOrderInput input, CancellationToken cancellationToken = default);

    Task<OrderView> PayOrderAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken cancellationToken = default);

    Task<OrderView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundView> RefundOrderAsync(string callerId, bool isOperator, int orderId, RefundOrderInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<OrderView?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<SavedCardView> SaveCardAsync(string ownerId, SaveCardInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string ownerId, CancellationToken cancellationToken = default);

    Task<SavedCardView> DeleteCardAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
