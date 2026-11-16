namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of the money movement for an order. The order itself is still the
/// existing <see cref="OrderAggregate.Order"/> aggregate; this value describes the
/// Finix side of that order.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Canceled = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    PaymentFailed = 6
}
