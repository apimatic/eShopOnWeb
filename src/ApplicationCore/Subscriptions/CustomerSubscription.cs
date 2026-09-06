using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// One shopper's enrollment in a <see cref="SubscriptionPlan"/>, as held by the billing system.
/// </summary>
/// <param name="Id">Billing-system subscription id.</param>
/// <param name="PlanHandle">Handle of the plan the shopper is enrolled in.</param>
/// <param name="PlanName">Display name of that plan.</param>
/// <param name="Price">Recurring price of the subscription.</param>
/// <param name="Currency">ISO currency code of the subscription.</param>
/// <param name="State">Lifecycle state reported by the billing system (e.g. "active").</param>
/// <param name="PaymentCollectionMethod">How the billing system collects for this subscription (e.g. "remittance").</param>
/// <param name="IsLive">True when the subscription still entitles the shopper (i.e. it is not cancelled/expired).</param>
/// <param name="NextBillingDate">When the current billing period ends and the next assessment is due.</param>
/// <param name="CreatedAt">When the subscription was created.</param>
public record CustomerSubscription(
    int Id,
    string? PlanHandle,
    string? PlanName,
    decimal? Price,
    string? Currency,
    string? State,
    string? PaymentCollectionMethod,
    bool IsLive,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);
