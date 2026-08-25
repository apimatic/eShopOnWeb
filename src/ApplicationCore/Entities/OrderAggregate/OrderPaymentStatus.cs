namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    Refunded = 4,
    PartiallyRefunded = 5
}
