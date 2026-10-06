using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public enum PayOrderOutcome
{
    Paid,
    AlreadyPaid,
    Refused,
    CardDetailsRejected,
    AuthenticationNotSupported,
    // Accepted by the provider, or sent without a readable answer: do not pay again, check back later.
    Pending,
    InProgress,
    ProviderUnavailable,
    AmountNotChargeable,
    OrderNotFound
}

public sealed record PayOrderResult(
    PayOrderOutcome Outcome,
    int OrderId,
    OrderPaymentStatus PaymentStatus,
    OrderPaymentAttempt? Attempt,
    string Message);

public enum RefundOrderOutcome
{
    Received,
    // Sent, but the provider's answer could not be read; the amount stays reserved until settled.
    Pending,
    Rejected,
    ProviderUnavailable,
    ExceedsRefundable,
    InvalidAmount,
    NotPaid,
    InProgress,
    OrderNotFound
}

public sealed record RefundOrderResult(
    RefundOrderOutcome Outcome,
    int OrderId,
    OrderRefund? Refund,
    long RefundableMinorUnits,
    string? Currency,
    string Message);
