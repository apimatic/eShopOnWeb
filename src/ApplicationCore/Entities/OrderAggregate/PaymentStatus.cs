namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    AuthorizationExpired = 2,
    Voided = 3,
    Captured = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Failed = 7,
    CaptureFailed = 8
}
