using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>ISO 4217 code of the currency orders are charged in.</summary>
    string Currency { get; }

    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Pays the order total by card. Never charges twice: a paid order returns its existing payment, and an
    /// attempt whose outcome is unknown is settled with its original idempotency key before anything else.
    /// </summary>
    Task<PayOrderResult> PayAsync(int orderId, string buyerId, EncryptedCardDetails card, string returnUrl);

    /// <summary>Refunds <paramref name="amount"/> (or everything still refundable when null) on a paid order.</summary>
    Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string requestedBy);

    /// <summary>The buyer's orders with their payments and refunds, newest first.</summary>
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>An order with its payments, refunds and every raw provider response kept for them.</summary>
    Task<Order?> GetOrderWithPaymentsAsync(int orderId, CancellationToken cancellationToken);
}
