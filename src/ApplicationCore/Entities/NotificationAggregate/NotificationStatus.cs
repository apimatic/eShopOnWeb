namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's mirror of the messaging provider's delivery lifecycle for a single message,
/// plus two local-only states that describe failures that happened before the provider
/// ever assigned an outcome.
/// </summary>
public enum NotificationStatus
{
    /// <summary>Created locally, not yet handed to the provider.</summary>
    Pending = 0,

    /// <summary>The request to create the message never succeeded (network/4xx); no provider id exists.</summary>
    SendError = 1,

    Queued = 2,
    Accepted = 3,
    Scheduled = 4,
    Sending = 5,
    Sent = 6,
    Delivered = 7,
    Undelivered = 8,
    Failed = 9,
    Canceled = 10,
    Read = 11,

    /// <summary>Provider returned a status string eShop does not recognise.</summary>
    Unknown = 12
}
