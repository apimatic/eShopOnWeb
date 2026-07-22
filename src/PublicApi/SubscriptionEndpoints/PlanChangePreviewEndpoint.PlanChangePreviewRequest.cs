using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    /// <summary>The plan to move to, given as its stable handle or numeric identifier.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> (prorated, the default) or <c>NextRenewal</c>.</summary>
    public string? Timing { get; set; }

    /// <summary>
    /// Alternative to <see cref="Timing"/>: <c>false</c> defers the change to the next renewal.
    /// </summary>
    public bool? Prorate { get; set; }

    public PlanChangeTiming ResolveTiming()
        => SubscriptionRequestParser.ParsePlanChangeTiming(Timing, Prorate);

    public static PlanChangePreviewRequest From(SubscriptionRequestBody body) => new()
    {
        PlanHandle = body.GetString(SubscriptionRequestParser.PlanNames) ?? string.Empty,
        Timing = body.GetString(SubscriptionRequestParser.TimingNames),
        Prorate = body.GetBoolean(SubscriptionRequestParser.ProrationNames)
    };
}
