using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderData data, CancellationToken cancellationToken = default);

    Task<OrderDto> PayOrderAsync(PayOrderData data, CancellationToken cancellationToken = default);

    Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundDto> RefundOrderAsync(RefundOrderData data, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethodDto> SavePaymentMethodAsync(SavePaymentMethodData data, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethodDto>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
