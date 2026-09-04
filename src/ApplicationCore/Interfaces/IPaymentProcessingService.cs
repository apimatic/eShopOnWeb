using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Integrations.Reconciliation;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestration of the PayPal payment flows over eShop orders: authorize (hold) at pay time,
/// capture (take) at fulfilment, void (release) on cancel, and refunds after fulfilment - plus
/// saving and removing cards.
/// </summary>
public interface IPaymentProcessingService
{
    /// <summary>Places an order from catalog items; it starts in <see cref="OrderStatus.AwaitingPayment"/>.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address shipTo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the exact order total with PayPal (a hold, no money taken).
    /// Idempotent: calling it again on an already-authorized (and still valid) payment
    /// returns the existing authorization instead of holding funds twice.
    /// </summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, string? paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator flow: captures the authorized money and marks the order fulfilled.
    /// A stale authorization is renewed first (when the payment used a saved card);
    /// when it cannot be renewed the failure is reported with an operator-actionable message.
    /// </summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator flow: cancel before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment in full or in part; repeating the same idempotency key never refunds twice.</summary>
    Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders (newest first) with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Saves (vaults) a card for the buyer; only the vault token + safe description are kept.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>All saved cards of the buyer.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetBuyerCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card so it no longer appears and can no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Liner up PayPal's transaction record for a date range against eShop payments.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One line of a place-order request: which catalog item and how many.</summary>
public record PlaceOrderLine(int CatalogItemId, int Quantity);
