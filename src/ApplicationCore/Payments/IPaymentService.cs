using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentService
{
    Task<OrderWithPayment> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<Payment> PayOrderAsync(
        string buyerId,
        int orderId,
        int? savedPaymentMethodId,
        PaymentCard? card,
        CancellationToken cancellationToken = default);

    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundResult> RefundOrderAsync(
        int orderId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(
        string buyerId,
        SavedCardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
