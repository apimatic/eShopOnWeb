namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    PendingAuthorization,
    Authorized,
    Captured,
    Voided,
    PartiallyRefunded,
    Refunded,
    Failed
}
