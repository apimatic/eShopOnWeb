using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Local record linking an eShopOnWeb user to a Maxio Advanced Billing
/// subscription. Maxio remains the billing system of record; this row is a
/// fast lookup/idempotency cache keyed by the ASP.NET Identity user id.
/// </summary>
public sealed class AppUserSubscription
{
    public int Id { get; private set; }

    /// <summary>ASP.NET Identity user id.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Maxio customer id this user maps to.</summary>
    public int MaxioCustomerId { get; private set; }

    /// <summary>Maxio customer reference (equals the user id).</summary>
    public string MaxioCustomerReference { get; private set; } = string.Empty;

    /// <summary>Maxio subscription id.</summary>
    public int MaxioSubscriptionId { get; private set; }

    /// <summary>Handle of the subscribed product (plan) at signup time.</summary>
    public string ProductHandle { get; private set; } = string.Empty;

    /// <summary>Name of the subscribed product at signup time.</summary>
    public string ProductName { get; private set; } = string.Empty;

    /// <summary>Recurring price in cents at signup time.</summary>
    public int PriceInCents { get; private set; }

    /// <summary>Currency at signup time.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Last known Maxio subscription state.</summary>
    public string State { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public AppUserSubscription(string userId, int maxioCustomerId, string maxioCustomerReference,
        int maxioSubscriptionId, string productHandle, string productName, int priceInCents, string currency,
        string state)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioCustomerReference = maxioCustomerReference;
        MaxioSubscriptionId = maxioSubscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Currency = currency;
        State = state;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void UpdateState(string state)
    {
        State = state;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
