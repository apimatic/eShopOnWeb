using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken cancellationToken = default);
    Task<PaymentResult> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default);
    Task<FulfilmentResult> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<CancelResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<RefundResult> RefundOrderAsync(string buyerId, int orderId, RefundOrderCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<SavedCardResult> SaveCardAsync(string buyerId, SaveCardCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedCardResult>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
