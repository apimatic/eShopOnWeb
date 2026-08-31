using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items at current catalog prices.
    /// The order starts in <see cref="OrderStatus.PendingPayment"/>.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total at PayPal, either with raw card
    /// details or with one of the shopper's saved cards. Idempotent: paying an
    /// already-authorized order returns the existing authorization.</summary>
    Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, PayPalCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the held funds. A stale authorization is
    /// renewed first; one that cannot be renewed yields an actionable error.</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, voiding the hold so no
    /// money ever moves.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full (amount null) or in part.
    /// Idempotent per caller-supplied key.</summary>
    Task<(Payment Payment, PaymentRefund Refund, bool AlreadyExisted)> RefundOrderAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Payment?> GetActivePaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator report: PayPal's own record of transactions over the range,
    /// lined up against eShop orders.</summary>
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
