using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund, plus reads. Each action is separately invocable and idempotent
/// in effect. All shopper-scoped operations take the caller's <c>buyerId</c> and act only on
/// that shopper's data.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipTo, CancellationToken ct = default);

    /// <summary>Authorize (hold) the order total. Idempotent: a second call returns the existing hold.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct = default);

    /// <summary>Capture at fulfilment; renews a stale hold first. Operator action.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Release the hold before fulfilment. Operator action.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a captured payment, full or partial, under a caller idempotency key.</summary>
    Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken ct = default);
}

/// <summary>Saves, lists and removes a shopper's vaulted cards.</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default);
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}

/// <summary>Builds the reconciliation report over a date range.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken ct = default);
}
