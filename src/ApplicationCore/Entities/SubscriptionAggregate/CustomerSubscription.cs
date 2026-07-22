using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A provider-agnostic snapshot of a customer's subscription as returned by the billing provider.
/// This is a read model — the integration is stateless (§8) and treats the provider as the system
/// of record, so this is never persisted in eShopOnWeb.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id, string state, int customerId, string? customerReference,
        string productHandle, string productName, decimal productPrice, string interval,
        DateTimeOffset? currentPeriodEndsAt, bool cancelAtEndOfPeriod, DateTimeOffset? canceledAt)
    {
        Id = id;
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        ProductPrice = productPrice;
        Interval = interval;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        CanceledAt = canceledAt;
    }

    public int Id { get; }

    /// <summary>The provider subscription state, e.g. <c>active</c>, <c>on_hold</c>, <c>canceled</c>.</summary>
    public string State { get; }

    public int CustomerId { get; }

    public string? CustomerReference { get; }

    public string ProductHandle { get; }

    public string ProductName { get; }

    /// <summary>The plan price in whole currency units (dollars).</summary>
    public decimal ProductPrice { get; }

    public string Interval { get; }

    /// <summary>When the current billing period ends — i.e. the next billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    public bool CancelAtEndOfPeriod { get; }

    public DateTimeOffset? CanceledAt { get; }

    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(State, "trialing", StringComparison.OrdinalIgnoreCase);
}
