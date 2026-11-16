using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IFinixPaymentService
{
    Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> PayOrderAsync(string buyerId, PayOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> FulfilOrderAsync(FulfilOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> RefundOrderAsync(RefundOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<ReconciliationResponse> ReconcileAsync(ReconciliationRequest request, CancellationToken cancellationToken = default);

    Task<PaymentMethodDto> SavePaymentMethodAsync(string buyerId, SavePaymentMethodRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
