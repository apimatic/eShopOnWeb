using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Places a hold for the order total using either one-off card details or one of the
    /// shopper's saved cards. Idempotent: repeating it returns the existing authorization.
    /// </summary>
    Task<Payment> AuthorizeOrderPaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: captures the held funds. Renews a stale authorization when PayPal
    /// still allows it; otherwise throws AuthorizationNotRenewableException.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a fulfilled order, in full or in part. The idempotency key guarantees a
    /// repeated request under the same key never refunds twice.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default);

    Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
