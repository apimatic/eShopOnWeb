using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Places a hold on the order total via PayPal. Idempotent: calling again for an
    /// already-authorized order returns the existing payment without re-authorizing.
    /// </summary>
    Task<Payment> AuthorizePaymentAsync(Order order, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the held funds at fulfilment. Renews a stale authorization first;
    /// throws AuthorizationRenewalException when it can no longer be renewed.
    /// Idempotent: an already-captured payment is returned as-is.
    /// </summary>
    Task<Payment> CapturePaymentAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Releases the hold without moving money. Idempotent.</summary>
    Task<Payment?> VoidPaymentAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment in full (amount null) or in part.
    /// Idempotent per idempotencyKey: a repeated key returns the original refund.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(Order order, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
