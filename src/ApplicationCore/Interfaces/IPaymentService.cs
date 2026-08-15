using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: authorize (hold), fulfil (capture), cancel (void)
/// and refund. All operations are idempotent in effect against a double-click.
/// </summary>
public interface IPaymentService
{
    /// <summary>Authorize (hold) the order total. Shopper-scoped: acts only on the caller's order.</summary>
    Task<OrderPaymentState> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Fulfil the order: capture the held funds, renewing a stale authorization if needed. Operator action.</summary>
    Task<OrderPaymentState> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel before fulfilment: release the held funds. Operator action.</summary>
    Task<OrderPaymentState> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full or in part. Shopper-scoped. The idempotency key
    /// makes a repeat a no-op while distinct keys allow distinct partial refunds.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Load an order plus its payment, scoped to the caller. Returns null if not the caller's.</summary>
    Task<OrderPaymentState?> GetForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>All of the caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentState>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
