using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, either with one-off card details or with one of
    /// the shopper's saved cards. Repeating the call for an already-authorized order returns
    /// the existing payment instead of authorizing again.
    /// </summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the authorized funds. Renews a stale authorization first; throws
    /// AuthorizationRenewalException when it can no longer be renewed.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundOrderAsync(int orderId, string idempotencyKey, decimal? amount, string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPaymentSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<(Order Order, Payment? Payment)?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public record OrderItemRequest(int CatalogItemId, int Units);

public record OrderPaymentSummary(Order Order, Payment? Payment);
