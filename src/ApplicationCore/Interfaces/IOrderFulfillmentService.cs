using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Result of an operator fulfilment action against an order.</summary>
public record OrderActionResult(ActionOutcome Outcome, string? Error);

/// <summary>
/// Operator actions that move an order along. Each tells the shopper what happened; a failure to
/// message never fails the underlying operation.
/// </summary>
public interface IOrderFulfillmentService
{
    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with the
    /// provider asking how the delivery went a few days later.
    /// </summary>
    Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not yet gone out so it
    /// never reaches them.
    /// </summary>
    Task<OrderActionResult> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}
