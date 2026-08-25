namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    PendingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Voided = 3,
    Refunded = 4,
    PartiallyRefunded = 5
}
