using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentService
{
    Task<OrderDto> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderItemInput> items,
        ShippingAddressInput? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<OrderDto> PayOrderAsync(
        string buyerId,
        int orderId,
        CardInput? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<OrderDto> FulfilOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<OrderDto> CancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<RefundDto> RefundOrderAsync(
        string buyerId,
        int orderId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<SavedCardDto> SavePaymentMethodAsync(
        string buyerId,
        CardInput card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardDto>> GetPaymentMethodsAsync(
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
