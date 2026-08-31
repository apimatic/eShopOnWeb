namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    None = 0,
    AwaitingPayment = 1,
    Authorized = 2,
    PayerActionRequired = 3,
    AuthorizationRenewalRequired = 4,
    Fulfilled = 5,
    PartiallyRefunded = 6,
    Refunded = 7,
    Cancelled = 8
}
