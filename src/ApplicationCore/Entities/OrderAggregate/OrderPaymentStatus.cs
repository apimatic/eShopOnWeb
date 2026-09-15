namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    Cancelled,
    PartiallyRefunded,
    Refunded,
    Failed
}
