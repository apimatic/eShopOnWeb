namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of a Finix payment attached to an order.
/// </summary>
public enum PaymentStatus
{
    AwaitingAuthorization = 0,
    Authorized = 1,
    Captured = 2,
    Voided = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Failed = 6
}
