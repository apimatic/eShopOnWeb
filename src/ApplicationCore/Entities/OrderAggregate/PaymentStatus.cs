namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    NotStarted = 0,
    Authorized = 1,
    AuthorizationUnrecoverable = 2,
    Captured = 3,
    Voided = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
