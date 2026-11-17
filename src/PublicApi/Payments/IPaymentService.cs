using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPaymentService
{
    Task<CreateOrderResult> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken);

    Task<PaymentView> PayOrderAsync(
        string buyerId,
        PayOrderRequest request,
        CancellationToken cancellationToken);

    Task<PaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<PaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundResult> RefundOrderAsync(
        string buyerId,
        RefundOrderRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationResult> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<SavedPaymentMethodView> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedPaymentMethodView>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken);

    Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken);
}
