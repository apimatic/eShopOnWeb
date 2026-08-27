namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Voided = 3,
    Failed = 4
}
