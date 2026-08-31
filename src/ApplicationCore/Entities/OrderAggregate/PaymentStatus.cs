namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Declined = 2,
    Captured = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Voided = 6
}
