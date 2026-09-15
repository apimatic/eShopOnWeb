using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records that a notification's content was disposed of at the shopper's request. Kept as its own
/// append-only record (rather than a flag mutated on the notification) so the fact survives cleanly:
/// the disposal is written once and read back to mark the notification as redacted, while the
/// notification's own record — that a message was sent and what became of it — is left untouched.
/// </summary>
public class NotificationContentDisposal : BaseEntity, IAggregateRoot
{
    private NotificationContentDisposal() { } // EF only

    public NotificationContentDisposal(int notificationId)
    {
        Guard.Against.NegativeOrZero(notificationId, nameof(notificationId));
        NotificationId = notificationId;
    }

    public int NotificationId { get; private set; }

    public DateTimeOffset DisposedAt { get; private set; } = DateTimeOffset.UtcNow;
}
