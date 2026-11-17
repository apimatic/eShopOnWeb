using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<OrderPaymentView> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken = default);

    Task<OrderPaymentView> PayOrderAsync(PayOrderCommand command, CancellationToken cancellationToken = default);

    Task<OrderPaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderPaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundView> RefundOrderAsync(RefundOrderCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethodView> SaveCardAsync(SaveCardCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethodView>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
