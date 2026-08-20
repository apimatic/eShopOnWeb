using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    private UserSubscription()
    {
    }

    public UserSubscription(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        DateTime createdAtUtc,
        string enrollmentToken)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        EnrollmentToken = enrollmentToken;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string EnrollmentToken { get; private set; } = string.Empty;

    public void Complete(long maxioCustomerId, long maxioSubscriptionId, DateTime updatedAtUtc)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RenewReservation(string enrollmentToken, DateTime updatedAtUtc)
    {
        EnrollmentToken = enrollmentToken;
        UpdatedAtUtc = updatedAtUtc;
    }
}
