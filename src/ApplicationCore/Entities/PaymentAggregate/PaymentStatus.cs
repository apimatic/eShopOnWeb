namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentStatus
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Authorized = "Authorized";
    public const string Captured = "Captured";
    public const string Cancelled = "Cancelled";
    public const string PartiallyRefunded = "PartiallyRefunded";
    public const string Refunded = "Refunded";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
}
