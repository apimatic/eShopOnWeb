namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery outcome of a notification. Values mirror the provider's own message statuses so the
/// state we hold stays faithful to the state the provider owns, plus two local-only values for cases
/// the provider never assigns a status to.
/// </summary>
public enum NotificationStatus
{
    /// <summary>Local only: the send was attempted but the provider call itself failed, so no message was ever accepted.</summary>
    SubmitFailed = 0,

    // --- Values reflecting the provider's message status ---
    Queued = 1,
    Sending = 2,
    Sent = 3,
    Delivered = 4,
    Undelivered = 5,
    Failed = 6,
    Scheduled = 7,
    Canceled = 8,
    Accepted = 9,
    Receiving = 10,
    Received = 11,
    Read = 12,
    PartiallyDelivered = 13,

    /// <summary>Local only: the provider returned a status we do not recognise.</summary>
    Unknown = 99
}

public static class NotificationStatusExtensions
{
    /// <summary>
    /// True once a message has reached a state that will not change on its own, so there is no point
    /// asking the provider to refresh it again.
    /// </summary>
    public static bool IsTerminal(this NotificationStatus status) => status switch
    {
        NotificationStatus.Delivered => true,
        NotificationStatus.Undelivered => true,
        NotificationStatus.Failed => true,
        NotificationStatus.Canceled => true,
        NotificationStatus.Read => true,
        NotificationStatus.SubmitFailed => true,
        _ => false
    };

    /// <summary>True when the message did not reach the shopper, i.e. it is a candidate for an operator re-send.</summary>
    public static bool DidNotReachRecipient(this NotificationStatus status) => status switch
    {
        NotificationStatus.Undelivered => true,
        NotificationStatus.Failed => true,
        NotificationStatus.SubmitFailed => true,
        _ => false
    };
}
