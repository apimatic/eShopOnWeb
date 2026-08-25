namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum OrderPaymentStatus
{
    PendingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    FullyRefunded = 5
}
