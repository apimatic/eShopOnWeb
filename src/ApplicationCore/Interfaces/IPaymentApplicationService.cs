using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentApplicationService
{
    Task<OrderResponse> CreateOrderAsync(string buyerId, CreateOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<PaymentResponse> AuthorizeOrderAsync(string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<PaymentResponse> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentResponse> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundOperationResponse> RefundOrderAsync(string buyerId, bool isAdministrator, int orderId,
        RefundCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, SaveCardCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethodResponse>> ListPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
