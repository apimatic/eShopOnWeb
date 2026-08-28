using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPaymentService
{
    Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken);
    Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken);
    Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken);
    Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken);
    Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
