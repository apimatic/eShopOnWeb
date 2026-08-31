namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    NotRequired = 0,
    AwaitingPayment = 1,
    AuthorizationPending = 2,
    Authorized = 3,
    CapturePending = 4,
    Fulfilled = 5,
    Cancelled = 6,
    PartiallyRefunded = 7,
    Refunded = 8
}
