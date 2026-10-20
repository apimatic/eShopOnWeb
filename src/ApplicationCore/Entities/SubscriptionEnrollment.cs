using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum EnrollmentStatus
{
    /// <summary>The user's claim is held; the billing writes are in flight or were interrupted.</summary>
    Claimed,
    /// <summary>The subscription exists at the billing provider.</summary>
    Active,
    /// <summary>A billing write may or may not have landed; it must be settled by a re-read.</summary>
    Unknown
}

/// <summary>
/// One row per shopper (primary key = user id). Inserting the row is the claim that stops a second
/// concurrent Subscribe from reaching the billing provider.
/// </summary>
public class SubscriptionEnrollment
{
    // Required by EF Core
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string planHandle, string customerReference, string subscriptionReference, DateTimeOffset now)
    {
        UserId = userId;
        PlanHandle = planHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = EnrollmentStatus.Claimed;
        CreatedUtc = now;
        UpdatedUtc = now;
    }

    public string UserId { get; private set; } = string.Empty;
    public string PlanHandle { get; private set; } = string.Empty;
    public EnrollmentStatus Status { get; private set; }
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string? SubscriptionState { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset UpdatedUtc { get; private set; }

    public void RecordCustomer(int customerId, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        UpdatedUtc = now;
    }

    public void MarkActive(int subscriptionId, int? customerId, string? state, DateTimeOffset now)
    {
        Status = EnrollmentStatus.Active;
        MaxioSubscriptionId = subscriptionId;
        if (customerId.HasValue) MaxioCustomerId = customerId;
        SubscriptionState = state;
        UpdatedUtc = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = EnrollmentStatus.Unknown;
        UpdatedUtc = now;
    }
}
