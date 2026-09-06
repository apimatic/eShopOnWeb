using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record CreateSubscriptionResponse(
    string CorrelationId,
    int SubscriptionId,
    string State,
    string ProductHandle,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    decimal? ProductPricePerMonth,
    string? Reference);
