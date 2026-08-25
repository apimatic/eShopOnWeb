namespace Microsoft.eShopWeb.ApplicationCore.Enums;

public enum PaymentStatus
{
    PendingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Voided = 3,
    Failed = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
