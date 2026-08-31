using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: authorize at checkout, capture at fulfilment,
/// void on cancel, refund on return. All operations are idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Authorize (hold) the order total, with raw card details or a saved card.</summary>
    Task<Payment> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>Capture the held funds (operator). Renews a stale authorization first.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Cancel before fulfilment, releasing any held funds (operator).</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund the captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken ct);
}
