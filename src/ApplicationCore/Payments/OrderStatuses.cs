namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class OrderStatuses
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Authorized = "Authorized";
    public const string Fulfilled = "Fulfilled";
    public const string Cancelled = "Cancelled";
    public const string PartiallyRefunded = "PartiallyRefunded";
    public const string Refunded = "Refunded";
}
