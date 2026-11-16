namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class OrderStatus
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Authorized = "Authorized";
    public const string Fulfilled = "Fulfilled";
    public const string Cancelled = "Cancelled";
    public const string PartiallyRefunded = "PartiallyRefunded";
    public const string Refunded = "Refunded";
}
