namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentState
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Voided = 5
}

public enum FulfilmentState
{
    Pending = 0,
    Fulfilled = 1,
    Cancelled = 2
}
