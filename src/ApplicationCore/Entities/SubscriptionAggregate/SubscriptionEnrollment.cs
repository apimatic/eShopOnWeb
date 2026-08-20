using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionEnrollment : BaseEntity
{
    private SubscriptionEnrollment()
    {
    }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string subscriptionReference,
        string operationId,
        DateTimeOffset now)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        OperationId = operationId;
        PendingSince = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string OperationId { get; private set; } = string.Empty;
    public DateTimeOffset PendingSince { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsComplete => MaxioCustomerId.HasValue && MaxioSubscriptionId.HasValue;

    public void Claim(string operationId, DateTimeOffset now)
    {
        OperationId = operationId;
        PendingSince = now;
        UpdatedAt = now;
    }

    public void Complete(int maxioCustomerId, int maxioSubscriptionId, DateTimeOffset now)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedAt = now;
    }
}
