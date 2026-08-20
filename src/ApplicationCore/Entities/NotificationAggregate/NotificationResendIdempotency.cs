using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendIdempotency : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private NotificationResendIdempotency() { }
    #pragma warning restore CS8618

    public NotificationResendIdempotency(int originalNotificationId, string idempotencyKey, int resultNotificationId)
    {
        Guard.Against.OutOfRange(originalNotificationId, nameof(originalNotificationId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.OutOfRange(resultNotificationId, nameof(resultNotificationId), 1, int.MaxValue);

        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultNotificationId = resultNotificationId;
    }

    public int OriginalNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }
}
