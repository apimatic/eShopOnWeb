namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public static class NotificationDeliveryStatus
{
    public const string Pending = "pending";
    public const string ProviderError = "provider_error";
    public const string Scheduled = "scheduled";

    public static bool DidNotReach(string status) =>
        status is "failed" or "undelivered" or ProviderError;

    public static bool MayStillBeSent(string status) =>
        status is "accepted" or "scheduled" or "queued" or "sending" or Pending;
}
