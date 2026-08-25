namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Authorized = "Authorized";
    public const string Captured = "Captured";
    public const string Voided = "Voided";
    public const string RefundedFull = "RefundedFull";
    public const string RefundedPartial = "RefundedPartial";
    public const string AuthorizationExpired = "AuthorizationExpired";
}
