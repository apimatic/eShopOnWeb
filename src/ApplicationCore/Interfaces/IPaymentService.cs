using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderDto> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderItemInput> items,
        ShippingAddressInput? shippingAddress,
        CancellationToken cancellationToken);

    Task<OrderDto> PayOrderAsync(
        string buyerId,
        int orderId,
        PayOrderInput input,
        CancellationToken cancellationToken);

    Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundResult> RefundOrderAsync(
        string callerBuyerId,
        bool callerIsAdministrator,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationReportDto> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<SavedCardDto> SavePaymentMethodAsync(
        string buyerId,
        CardInput card,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCardDto>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
