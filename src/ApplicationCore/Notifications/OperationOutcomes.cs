using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Outcome of an operator re-send.</summary>
public class ResendOutcome
{
    private ResendOutcome() { }

    /// <summary>True when the notification to re-send does not exist.</summary>
    public bool NotFound { get; private init; }

    /// <summary>
    /// The notification produced (for a fresh idempotency key) or the one previously produced under
    /// the same key (for an idempotent replay).
    /// </summary>
    public OrderNotification? Notification { get; private init; }

    /// <summary>True when this call reused an earlier result under the same idempotency key.</summary>
    public bool IdempotentReplay { get; private init; }

    public static ResendOutcome NotFoundResult() => new() { NotFound = true };
    public static ResendOutcome Sent(OrderNotification notification) => new() { Notification = notification };
    public static ResendOutcome Replayed(OrderNotification notification) =>
        new() { Notification = notification, IdempotentReplay = true };
}

/// <summary>Outcome of disposing of a message's content.</summary>
public class ContentDisposalOutcome
{
    private ContentDisposalOutcome() { }

    public bool NotFound { get; private init; }
    public OrderNotification? Notification { get; private init; }

    public static ContentDisposalOutcome NotFoundResult() => new() { NotFound = true };
    public static ContentDisposalOutcome Disposed(OrderNotification notification) =>
        new() { Notification = notification };
}
