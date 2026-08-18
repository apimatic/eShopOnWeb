namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local, application-owned delivery states for a notification. Once the provider has
/// accepted a message its own status wire value (queued, sent, delivered, undelivered,
/// failed, scheduled, canceled, ...) is stored verbatim; these constants cover the states
/// that exist before or independently of a provider status.
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>Raised locally, not yet handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The provider refused to accept the message at send time (no Sid was issued).</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A scheduled message was called off before it went out.</summary>
    public const string Canceled = "canceled";
}
