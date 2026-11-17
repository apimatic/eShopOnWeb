namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorizing = 1,
    Authorized = 2,
    Captured = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Failed = 7
}
