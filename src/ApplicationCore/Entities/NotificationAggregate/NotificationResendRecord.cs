using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records a caller-supplied idempotency key for an operator resend request,
/// so repeating the request under the same key does not send a second message.
/// </summary>
public class NotificationResendRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private NotificationResendRecord() { }

    public NotificationResendRecord(string idempotencyKey, string operatorId,
        int sourceNotificationId, int resultNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(operatorId, nameof(operatorId));

        IdempotencyKey = idempotencyKey;
        OperatorId = operatorId;
        SourceNotificationId = sourceNotificationId;
        ResultNotificationId = resultNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public string OperatorId { get; private set; }
    public int SourceNotificationId { get; private set; }
    public int ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
