namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    Failed = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Unknown = 7
}
