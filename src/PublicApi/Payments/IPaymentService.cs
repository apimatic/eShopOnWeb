using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPaymentService
{
    Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto> PayOrderAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundDto> RefundOrderAsync(string buyerId, int orderId, RefundOrderRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<ReconciliationDto> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<PaymentMethodDto> SavePaymentMethodAsync(string buyerId, SavePaymentMethodRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, Guid paymentMethodId, CancellationToken cancellationToken);
}
