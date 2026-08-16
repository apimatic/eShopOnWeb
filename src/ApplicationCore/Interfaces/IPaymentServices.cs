using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Places an order from catalog items, reusing the app's existing order/order-item model.</summary>
public interface IOrderPlacementService
{
    Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress, IReadOnlyCollection<OrderLine> lines,
        CancellationToken cancellationToken = default);
}

/// <summary>Drives the money movements over an order: authorize, capture at fulfilment, cancel, refund.</summary>
public interface IPaymentService
{
    /// <summary>Authorize (hold) the order total. Idempotent: a repeat never places a second hold.</summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Operator fulfils the order; the held funds are captured now. Renews a stale hold if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator cancels before fulfilment; the held funds are released.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, full or partial, under a caller idempotency key.</summary>
    Task<Refund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>Manages a shopper's saved cards (vault + safe metadata), scoped to their owner.</summary>
public interface IPaymentMethodService
{
    Task<CustomerPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card. Returns false if it does not belong to the caller / does not exist.</summary>
    Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>Builds the reconciliation report lining PayPal's transactions up against eShop orders.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
