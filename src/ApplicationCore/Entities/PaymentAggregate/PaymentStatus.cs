namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    None = 0,
    Authorized = 1,
    AuthorizationExpired = 2,
    Voided = 3,
    Captured = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
