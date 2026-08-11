using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Authorizes (holds) the order total. Pays with a raw <paramref name="card"/> for a one-off
    /// payment, or with one of the shopper's saved cards named by <paramref name="savedCardId"/>.
    /// Idempotent in effect: repeating it for an already-authorized order returns the same payment.
    /// </summary>
    Task<Payment> AuthorizeAsync(int orderId, string buyerId, int? savedCardId, CardDetails? card,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fulfils the order — this is when the money is actually taken (the authorization is captured).
    /// A stale authorization is renewed first; one that can no longer be renewed surfaces an
    /// operator-actionable error. Idempotent: capturing an already-captured order returns as-is.
    /// </summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels before fulfilment: voids the authorization so the held funds are released.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment, in full (null amount) or in part, under a caller-supplied
    /// idempotency key. Repeating the same key never refunds twice; the total refunded can never
    /// exceed what was captured.
    /// </summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
