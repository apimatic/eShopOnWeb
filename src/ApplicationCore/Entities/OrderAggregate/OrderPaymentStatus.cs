namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Canceled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
