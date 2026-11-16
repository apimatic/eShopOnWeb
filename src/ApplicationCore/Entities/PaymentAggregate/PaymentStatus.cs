namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    AuthorizationFailed = 2,
    AuthorizationExpired = 3,
    Captured = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Canceled = 7
}
