namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local status markers used before/without a provider status. Once the provider accepts
/// a message, its own status wire values (queued, sent, delivered, ...) are stored instead.
/// </summary>
public static class OrderNotificationStatus
{
    public const string Pending = "pending";
    public const string SendFailed = "send_failed";
}
