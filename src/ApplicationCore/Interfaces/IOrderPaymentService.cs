using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the order/payment lifecycle: place, authorize, fulfil (capture), cancel
/// (void), refund, and saved cards. All PayPal-owned state is persisted on <see cref="Payment"/>
/// so any later request can act on it.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken ct = default);

    Task<Payment> PayOrderAsync(int orderId, string buyerId, GatewayCardDetails? card,
        int? savedCardId, CancellationToken ct = default);

    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct = default);

    Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default);

    Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken ct = default);
}
