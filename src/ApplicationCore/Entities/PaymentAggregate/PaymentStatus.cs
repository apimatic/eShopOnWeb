namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    Authorized = 0,
    AuthorizationFailed = 1,
    Voided = 2,
    Captured = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
