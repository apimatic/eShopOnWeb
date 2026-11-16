using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken cancellationToken = default);

    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(string buyerId, int orderId,
        RefundInput input, CancellationToken cancellationToken = default);

    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<SavedCard> SaveCardAsync(string buyerId, Payments.CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
