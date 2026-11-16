namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentStatuses
{
    public const string AwaitingPayment = "AWAITING_PAYMENT";
    public const string Authorized = "AUTHORIZED";
    public const string Captured = "CAPTURED";
    public const string Cancelled = "CANCELLED";
    public const string Failed = "FAILED";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
    public const string Refunded = "REFUNDED";
    public const string OutcomeUnknown = "OUTCOME_UNKNOWN";
}
