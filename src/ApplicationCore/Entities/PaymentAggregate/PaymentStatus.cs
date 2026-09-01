namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    Authorized = 0,
    Captured = 1,
    Voided = 2,
    RequiresNewAuthorization = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
