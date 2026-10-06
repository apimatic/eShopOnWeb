using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record MaxioPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    string PriceDisplay,
    int? Interval,
    string? IntervalUnit,
    bool RequireCreditCard);

public record MaxioComponentDto(
    string Handle,
    string Name,
    string? Kind,
    string? UnitName,
    long? PricePerUnitInCents);

public record MaxioPlanCatalog(
    string ProductFamilyHandle,
    IReadOnlyList<MaxioPlanDto> Plans,
    IReadOnlyList<MaxioComponentDto> Components);

public record MaxioSubscriptionInfo(
    long Id,
    string PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? CollectionMethod);

public record MaxioSubscribeResult(
    MaxioSubscriptionInfo Subscription,
    bool Created);