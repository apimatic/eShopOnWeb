namespace Microsoft.eShopWeb.ApplicationCore.Constants;

/// <summary>
/// Provider message lifecycle statuses. Terminal statuses are those after which
/// the provider will not change the outcome again.
/// </summary>
public static class NotificationStatuses
{
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";

    public static bool IsTerminal(string? status) =>
        status is Delivered or Undelivered or Failed or Canceled;
}
