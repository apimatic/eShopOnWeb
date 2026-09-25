using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Places orders from catalog items and reports a shopper's orders with payment state.</summary>
public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderLine> lines,
        ShippingAddressInput? shipping, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
}

/// <summary>Drives the money movement for an order: authorize, capture (fulfil), void (cancel), refund.</summary>
public interface IOrderPaymentService
{
    /// <summary>Authorize the order total (a hold). Shopper-scoped. Idempotent per order.</summary>
    Task<OrderView> PayAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Operator action: fulfil the order — capture the held funds (renewing a stale hold first).</summary>
    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: cancel before fulfilment — release the held funds.</summary>
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Shopper-scoped: refund a captured payment (full or partial), under a caller idempotency key.</summary>
    Task<(string RefundId, OrderView Order)> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The caller's own order with payment state, or null if not theirs / not found.</summary>
    Task<OrderView?> GetOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken);
}

/// <summary>Saves, lists and removes a shopper's vaulted cards.</summary>
public interface ISavedCardService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken cancellationToken);
    /// <summary>Removes a saved card (and revokes it at PayPal). False when it isn't the caller's / not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}

/// <summary>Operator action: reconcile PayPal's transactions for a range against eShop orders.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
