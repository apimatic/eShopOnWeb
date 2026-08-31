namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Declined = 2,
    Captured = 3,
    Voided = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
