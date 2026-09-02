namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AuthorizationPending = 0,
    Authorized = 1,
    Voided = 2,
    Captured = 3,
    Failed = 4
}
