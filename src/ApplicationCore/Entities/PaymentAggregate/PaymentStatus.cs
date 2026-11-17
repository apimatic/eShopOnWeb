namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    Authorizing = 0,
    Authorized = 1,
    Capturing = 2,
    Captured = 3,
    Voiding = 4,
    Voided = 5,
    Unknown = 6,
    Failed = 7
}
