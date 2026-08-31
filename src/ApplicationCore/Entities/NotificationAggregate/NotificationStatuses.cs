namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known notification status values. The provider-owned ones use the provider's
/// wire values; SendFailed is local-only (the send never reached the provider).
/// </summary>
public static class NotificationStatuses
{
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string SendFailed = "send-failed";
}
