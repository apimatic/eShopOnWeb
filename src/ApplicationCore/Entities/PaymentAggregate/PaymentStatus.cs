namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    Authorized,
    AuthorizationExpired,
    Captured,
    Voided,
    PartiallyRefunded,
    Refunded
}
