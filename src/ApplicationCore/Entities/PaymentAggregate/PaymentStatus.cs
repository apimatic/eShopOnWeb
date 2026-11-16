namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement attached to an order. The order itself is the
/// commerce record; this state records what Finix owns (hold, capture, refunds).
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    PaymentFailed = 2,
    Fulfilled = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
