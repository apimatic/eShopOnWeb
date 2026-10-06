using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The minimal identity information needed to provision a billing customer
/// for an eShopOnWeb user.
/// </summary>
/// <param name="UserId">The stable eShopOnWeb user identifier (used as the billing-system reference).</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">Display name part; billing systems require it.</param>
/// <param name="LastName">Display name part; billing systems require it.</param>
public record SubscriberProfile(string UserId, string Email, string FirstName, string LastName);

/// <summary>
/// A subscribable plan as read from the billing system.
/// </summary>
/// <param name="Handle">The plan's stable handle (used to subscribe).</param>
/// <param name="Name">The plan's display name.</param>
/// <param name="PriceInCents">The recurring price in cents.</param>
/// <param name="Interval">Number of billing periods between charges.</param>
/// <param name="IntervalUnit">Billing period unit ("day" or "month").</param>
public record SubscriptionPlan(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);

/// <summary>
/// A subscription as reflected by the billing system.
/// </summary>
/// <param name="SubscriptionId">The billing system's subscription id.</param>
/// <param name="Reference">The idempotency reference assigned at creation.</param>
/// <param name="State">The billing system's subscription state (e.g. "active").</param>
/// <param name="PlanHandle">The subscribed plan's handle.</param>
/// <param name="PlanName">The subscribed plan's display name.</param>
/// <param name="PriceInCents">The recurring price in cents.</param>
/// <param name="NextBillingDate">When the current billing period ends (next renewal).</param>
/// <param name="NextAssessmentAt">When the billing system will next assess/charge.</param>
/// <param name="CustomerId">The billing system's customer id.</param>
/// <param name="CustomerReference">The billing system's customer reference (the eShopOnWeb user reference).</param>
public record SubscriptionSummary(
    int SubscriptionId,
    string Reference,
    string State,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? NextAssessmentAt,
    int CustomerId,
    string CustomerReference);