using System;

namespace Microsoft.eShopWeb.ApplicationCore.DTOs;

/// <summary>
/// A shopper's subscription as recorded in the billing system.
/// </summary>
public record CustomerSubscriptionDto(
    int Id,
    string State,
    string PlanName,
    string PlanHandle,
    long? PriceInCents,
    DateTimeOffset? NextBillingDate);
