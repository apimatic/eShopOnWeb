namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingAuthorization = 0,
    Authorized = 1,
    Captured = 2,
    Voided = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
}
