using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IOrderPaymentService
{
    Task<OrderPaymentResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderAddress? shipTo,
        CancellationToken cancellationToken = default);

    Task<OrderPaymentResult> PayAsync(
        int orderId,
        string buyerId,
        int? paymentMethodId,
        CardPaymentDetails? card,
        CancellationToken cancellationToken = default);

    Task<OrderPaymentResult> FulfilAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<OrderPaymentResult> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<RefundResult> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPaymentResult>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
