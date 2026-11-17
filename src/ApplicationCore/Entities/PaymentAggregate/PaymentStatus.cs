namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Canceled = 5,
    PaymentFailed = 6
}
