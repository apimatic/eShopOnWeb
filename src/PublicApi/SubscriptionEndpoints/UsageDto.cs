using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The running period-to-date usage of a metered component.</summary>
public class UsageDto
{
    public int? ComponentId { get; set; }

    public string? ComponentHandle { get; set; }

    public string? Name { get; set; }

    /// <summary>Units accrued so far this billing period — a count, not money.</summary>
    public int UnitBalance { get; set; }

    /// <summary>Unit price as a currency amount.</summary>
    public decimal? PricePerUnit { get; set; }

    /// <summary>What the accrued units will add to the next renewal invoice.</summary>
    public decimal? EstimatedCharge { get; set; }

    public static UsageDto FromSummary(ComponentUsageSummary summary)
    {
        return new UsageDto
        {
            ComponentId = summary.ComponentId,
            ComponentHandle = summary.ComponentHandle,
            Name = summary.Name,
            UnitBalance = summary.UnitBalance,
            PricePerUnit = summary.PricePerUnitInCents.HasValue ? summary.PricePerUnitInCents.Value / 100m : null,
            EstimatedCharge = summary.EstimatedCharge
        };
    }
}
