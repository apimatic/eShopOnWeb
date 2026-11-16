using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentApplicationService
{
    Task<int> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput shippingAddress,
        CancellationToken cancellationToken);

    Task<OrderSummary> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken);

    Task<OrderSummary> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderSummary> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundSummary> RefundAsync(string buyerId, int orderId, long amountCents, string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<SavedCardSummary> SaveCardAsync(string buyerId, CardInput card, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCardSummary>> ListCardsAsync(string buyerId, CancellationToken cancellationToken);

    Task DeleteCardAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
