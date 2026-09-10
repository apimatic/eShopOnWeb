using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// How a shopper is paying an order: either a one-off card, or one of their saved cards.
/// Exactly one form should be supplied.
/// </summary>
public record PaymentInstruction
{
    // One-off card
    public string? CardNumber { get; init; }
    public string? Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }

    // Or a saved card belonging to the shopper
    public int? SavedCardId { get; init; }
}

/// <summary>
/// Orchestrates the money movement over an order: authorize (hold), fulfil (capture), cancel
/// (void) and refund. Each is separately invocable. Operations are idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Authorizes the order total (a hold) for the order's own shopper.</summary>
    Task<Payment> PayAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken ct);

    /// <summary>Operator action: fulfil the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full or in part, for the order's own shopper.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);
}
