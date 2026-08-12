using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

public enum ResendOutcome
{
    /// <summary>A fresh resend was performed and produced a new message.</summary>
    Created,
    /// <summary>A prior request under the same idempotency key already produced this message; not re-sent.</summary>
    Replayed,
    /// <summary>No notification exists with the given id.</summary>
    NotFound,
    /// <summary>The target contact number has been removed; nothing may be sent to it again.</summary>
    ContactRemoved,
    /// <summary>The message content was disposed of and can no longer be re-sent.</summary>
    ContentDisposed
}

/// <summary>Outcome of a resend request, so the endpoint can map it to a precise HTTP result.</summary>
public class ResendResult
{
    public required ResendOutcome Outcome { get; init; }
    public Notification? Notification { get; init; }

    public static ResendResult Of(ResendOutcome outcome, Notification? notification = null) =>
        new() { Outcome = outcome, Notification = notification };
}
