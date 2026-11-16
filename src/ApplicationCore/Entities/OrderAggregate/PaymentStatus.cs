namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    AuthorizationFailed = 2,
    Cancelled = 3,
    Captured = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
