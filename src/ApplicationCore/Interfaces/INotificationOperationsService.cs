using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator actions on notifications: re-send, content disposal.</summary>
public interface INotificationOperationsService
{
    /// <summary>
    /// Re-sends the message of a notification. The caller-supplied idempotency key guarantees
    /// a repeated request under the same key does not send a second message; a fresh key is a
    /// genuine new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts the body at the provider (so it is no longer
    /// retrievable there) and clears it locally, keeping the record of what became of it.
    /// </summary>
    Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
}

public class ResendResult
{
    public ResendOutcome Outcome { get; set; }
    public OrderNotification? Notification { get; set; }
    public bool IsIdempotentReplay { get; set; }
    public string? Error { get; set; }
}

public enum ResendOutcome
{
    Sent,
    NotificationNotFound,
    DestinationNoLongerRegistered,
    NothingToResend
}

public class ContentDisposalResult
{
    public bool Succeeded { get; set; }
    public bool NotificationNotFound { get; set; }
    public string? Error { get; set; }
}
