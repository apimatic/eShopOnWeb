namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Canceled = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Failed = 6
}
