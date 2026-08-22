using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationIdempotencyRecord : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private NotificationIdempotencyRecord() { }
    #pragma warning restore CS8618

    public NotificationIdempotencyRecord(int sourceNotificationId, string idempotencyKey, int resultNotificationId)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(resultNotificationId, nameof(resultNotificationId));

        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultNotificationId = resultNotificationId;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }
}
