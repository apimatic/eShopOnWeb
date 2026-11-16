using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CreateOrderLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface IPaymentService
{
    Task<OrderInfo> CreateOrderAsync(string buyerId, IReadOnlyList<CreateOrderLine> items,
        PaymentAddress? shipToAddress, CancellationToken cancellationToken);

    Task<OrderInfo> PayOrderAsync(string buyerId, int orderId, PaymentCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken);

    Task<OrderInfo> FulfilOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderInfo> CancelOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundInfo> RefundOrderAsync(int orderId, long? amountInCents, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderInfo>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<OrderInfo> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken);

    Task<SavedCardInfo> SavePaymentMethodAsync(string buyerId, PaymentCardDetails card,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCardInfo>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
