namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public enum PaymentRecordStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    Refunded = 4,
    PartiallyRefunded = 5
}
