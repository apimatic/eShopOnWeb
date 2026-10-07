using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Charges the order total to the card. Never charges an order twice.</summary>
    Task<PayOrderResult> PayAsync(int orderId, string buyerId, EncryptedCard card, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives back <paramref name="amount"/> (or everything still refundable when null) on a paid order.
    /// The same <paramref name="idempotencyKey"/> on the same order always means the same refund.
    /// </summary>
    Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? reason, string? idempotencyKey,
        string requestedBy, CancellationToken cancellationToken = default);
}

public enum PayOrderOutcome
{
    Paid,
    AlreadyPaid,
    /// <summary>The card was refused or the payment failed; no money was taken and the shopper may try again.</summary>
    Declined,
    /// <summary>The provider rejected the card data itself.</summary>
    InvalidCard,
    /// <summary>The provider accepted the payment without a final outcome.</summary>
    Pending,
    /// <summary>Another request is paying this order right now.</summary>
    InProgress,
    /// <summary>The outcome could not be confirmed; paying again re-checks it without charging twice.</summary>
    Unknown,
    ProviderUnavailable,
    NotFound,
    Invalid
}

public sealed record PayOrderResult(
    PayOrderOutcome Outcome,
    int OrderId,
    OrderPaymentStatus? PaymentStatus,
    OrderPaymentAttempt? Attempt,
    string Message);

public enum RefundOrderOutcome
{
    Refunded,
    /// <summary>The refund for this idempotency key was already made.</summary>
    Existing,
    InProgress,
    Rejected,
    Unknown,
    ProviderUnavailable,
    NotPaid,
    NotFound,
    Invalid
}

public sealed record RefundOrderResult(
    RefundOrderOutcome Outcome,
    int OrderId,
    OrderPaymentStatus? PaymentStatus,
    OrderRefund? Refund,
    string? Message,
    long? RefundableMinor = null);
