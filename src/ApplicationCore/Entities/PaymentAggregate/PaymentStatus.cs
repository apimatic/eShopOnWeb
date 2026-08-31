namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Voided = 2,
    Captured = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Declined = 6,
    AuthorizationExpired = 7
}
