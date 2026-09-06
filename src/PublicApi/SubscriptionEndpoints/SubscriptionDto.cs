using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionDto(
    int Id,
    string ProductHandle,
    string State,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    decimal? ProductPricePerMonth,
    string? Reference);
