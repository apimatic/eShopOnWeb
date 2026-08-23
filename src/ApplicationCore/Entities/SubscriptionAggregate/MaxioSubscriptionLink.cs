using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioSubscriptionLink : BaseEntity, IAggregateRoot
{
    private MaxioSubscriptionLink() { }

    public MaxioSubscriptionLink(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        IntegrationStatus = MaxioSubscriptionIntegrationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public MaxioSubscriptionIntegrationStatus IntegrationStatus { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Activate(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        IntegrationStatus = MaxioSubscriptionIntegrationStatus.Active;
        FailureCode = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAmbiguous()
    {
        IntegrationStatus = MaxioSubscriptionIntegrationStatus.Ambiguous;
        FailureCode = "provider_outcome_unknown";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string failureCode)
    {
        IntegrationStatus = MaxioSubscriptionIntegrationStatus.Failed;
        FailureCode = failureCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum MaxioSubscriptionIntegrationStatus
{
    Pending,
    Active,
    Ambiguous,
    Failed
}
