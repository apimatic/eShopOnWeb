namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    Voided,
    PartiallyRefunded,
    Refunded,
    Failed
}
