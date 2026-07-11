using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsagePeriodSummaryDto
{
    public string ComponentHandle { get; init; } = string.Empty;
    public double? PeriodToDateQuantity { get; init; }
    public bool Available { get; init; }

    public static UsagePeriodSummaryDto FromDomain(UsagePeriodSummary summary) => new()
    {
        ComponentHandle = summary.ComponentHandle,
        PeriodToDateQuantity = summary.PeriodToDateQuantity,
        Available = summary.Available,
    };
}
