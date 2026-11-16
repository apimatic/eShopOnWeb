namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    PendingAuthorization = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    AuthorizationFailed = 6
}
