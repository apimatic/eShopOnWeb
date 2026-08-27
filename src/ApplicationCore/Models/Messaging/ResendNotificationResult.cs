using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

public enum ResendNotificationOutcome
{
    /// <summary>A fresh message was sent and recorded.</summary>
    Sent,

    /// <summary>The idempotency key was already used; the earlier resend is returned and nothing new was sent.</summary>
    Duplicate,

    /// <summary>The original notification could not be found.</summary>
    NotificationNotFound,

    /// <summary>The message content has been disposed of, so there is nothing to re-send.</summary>
    ContentDisposed,

    /// <summary>The destination number is no longer registered to the shopper; nothing may be sent to it.</summary>
    DestinationNoLongerRegistered
}

public class ResendNotificationResult
{
    public ResendNotificationOutcome Outcome { get; init; }

    /// <summary>The resend notification (newly created, or the earlier one for a duplicate key).</summary>
    public OrderNotification? Notification { get; init; }
}
