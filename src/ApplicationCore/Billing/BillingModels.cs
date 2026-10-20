using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>The authenticated caller, as the billing use cases need to know them.</summary>
public sealed record BillingUser(string UserName, string? Email);

public sealed record PlanInfo(
    string Handle,
    string? Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="IsTruncated">True when the provider listing was cut short by the page cap.</param>
public sealed record PlanListResult(IReadOnlyList<PlanInfo> Plans, bool IsTruncated);

public sealed record SubscriptionInfo(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);

public sealed record SubscribeResult(SubscriptionInfo Subscription, bool Created);

public sealed record BillingCustomer(int Id);

public sealed record NewBillingCustomer(string Reference, string Email, string FirstName, string LastName);
