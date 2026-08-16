using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// How to pay for an order: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be supplied. <see cref="SaveCard"/> vaults a one-off card
/// for later reuse.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedCardId, bool SaveCard);

/// <summary>
/// Drives the money movement for an order: authorize (hold) at pay time, capture at fulfilment,
/// release on cancel, and give back on refund. Every operation is idempotent in effect — a
/// double-click never authorizes or captures the shopper twice.
/// </summary>
public interface IPaymentService
{
    /// <summary>Authorize (hold) the order total. Shopper-scoped: only the order's owner may pay it. Returns the updated order.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Operator fulfilment: capture the held money, renewing a stale hold first if needed. Returns the updated order.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator cancellation before fulfilment: release the hold so no money moves. Returns the updated order.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a fulfilled order's capture, fully or partially, under a caller-supplied idempotency key.
    /// Returns the created (or, on a repeated key, the existing) refund together with its owning order.
    /// </summary>
    Task<(Refund Refund, Order Order)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
