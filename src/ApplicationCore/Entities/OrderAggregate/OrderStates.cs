namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    Canceled
}

public enum OrderFulfillmentStatus
{
    Pending,
    Fulfilled,
    Canceled
}
