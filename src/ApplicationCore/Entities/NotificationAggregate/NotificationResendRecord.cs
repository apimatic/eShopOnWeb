using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendRecord : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private NotificationResendRecord() { }
    #pragma warning restore CS8618

    public NotificationResendRecord(int originalNotificationId, string idempotencyKey, int resultNotificationId)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(resultNotificationId, nameof(resultNotificationId));

        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultNotificationId = resultNotificationId;
    }

    public int OriginalNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }
}
