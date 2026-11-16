using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public interface IOrderPaymentService
{
    Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentDto> AuthorizeAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentDto> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentDto> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundDto> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId, SavePaymentMethodRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
