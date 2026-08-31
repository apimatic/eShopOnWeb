namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>Delivery outcomes as reported by the messaging provider.</summary>
public static class NotificationStatuses
{
    public const string Queued = "queued";
    public const string Scheduled = "scheduled";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}
