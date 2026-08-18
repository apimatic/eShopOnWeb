using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives the money movement for an order: authorize (hold), fulfil (capture), cancel (void) and refund.
/// Each action is separately invocable and idempotent in effect. Shopper-scoped actions take the caller's
/// <c>buyerId</c> and act only on that shopper's own order; operator actions do not.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Authorize the order total against a one-off or saved card. Shopper-scoped. Idempotent.</summary>
    Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PaymentInstrument instrument,
        CancellationToken cancellationToken);

    /// <summary>Operator action: capture the held funds, renewing a stale authorization first if needed.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: void the hold before fulfilment, releasing the shopper's funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Shopper-scoped: refund the captured payment in full or in part, guarded by an idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);
}
