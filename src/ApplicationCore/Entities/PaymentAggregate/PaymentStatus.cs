namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AuthorizationPending = 0,
    Authorized = 1,
    AuthorizationFailed = 2,
    AuthorizationExpired = 3,
    Voided = 4,
    Captured = 5,
    PartiallyRefunded = 6,
    Refunded = 7
}
