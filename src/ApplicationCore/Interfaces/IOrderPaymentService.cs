using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PayOrderResult
{
    public required Order Order { get; init; }
    public required Payment Payment { get; init; }

    /// <summary>True when the order was already paid and the current state is simply being reported.</summary>
    public bool AlreadyPaid { get; init; }
}

public class FulfilOrderResult
{
    public required Order Order { get; init; }
    public required Payment Payment { get; init; }
    public bool AlreadyFulfilled { get; init; }

    /// <summary>True when a stale authorization had to be renewed before the capture succeeded.</summary>
    public bool AuthorizationRenewed { get; init; }
}

public class CancelOrderResult
{
    public required Order Order { get; init; }
    public Payment? Payment { get; init; }
    public bool AlreadyCancelled { get; init; }
}

public class RefundOrderResult
{
    public required Order Order { get; init; }
    public required Payment Payment { get; init; }
    public required PaymentRefund Refund { get; init; }

    /// <summary>True when the idempotency key was already used and the stored refund is being reported.</summary>
    public bool Replayed { get; init; }
}

/// <summary>
/// Orchestrates the money movement for an order: authorize at checkout, capture at
/// fulfilment, release on cancel, refund on return. All operations are idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Authorizes (holds) the order total, with raw card details or a saved card.</summary>
    Task<PayOrderResult> PayAsync(int orderId, string buyerId, CardPaymentDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>Captures the held funds. Renews a stale authorization when possible.</summary>
    Task<FulfilOrderResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels before fulfilment, releasing the shopper's held funds.</summary>
    Task<CancelOrderResult> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds the captured payment, in full (amount null) or in part.</summary>
    Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string? noteToPayer,
        CancellationToken cancellationToken = default);
}
