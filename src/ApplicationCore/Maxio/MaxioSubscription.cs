using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscription in Maxio Advanced Billing, as returned by the Create Subscription and
/// List Subscriptions endpoints.
/// </summary>
public record MaxioSubscription(
    int Id,
    string State,
    DateTimeOffset? NextBillingDate,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string Currency,
    int MaxioCustomerId,
    string? CustomerReference);
