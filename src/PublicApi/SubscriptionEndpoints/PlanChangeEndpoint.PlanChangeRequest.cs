using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The plan to move to, given as its stable handle or numeric identifier.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> (prorated, the default) or <c>NextRenewal</c>.</summary>
    public string? Timing { get; set; }

    /// <summary>
    /// Alternative to <see cref="Timing"/>: <c>false</c> defers the change to the next renewal.
    /// </summary>
    public bool? Prorate { get; set; }

    /// <summary>
    /// The payment due the customer was shown by the preview. When supplied it must still match a
    /// freshly taken quote, otherwise the change is refused as stale.
    /// </summary>
    public decimal? PreviewedPaymentDue { get; set; }

    public PlanChangeTiming ResolveTiming()
        => SubscriptionRequestParser.ParsePlanChangeTiming(Timing, Prorate);

    public static PlanChangeRequest From(SubscriptionRequestBody body) => new()
    {
        PlanHandle = body.GetString(SubscriptionRequestParser.PlanNames) ?? string.Empty,
        Timing = body.GetString(SubscriptionRequestParser.TimingNames),
        Prorate = body.GetBoolean(SubscriptionRequestParser.ProrationNames),
        PreviewedPaymentDue = ReadPreviewedPaymentDue(body)
    };

    /// <summary>
    /// The quoted amount may be confirmed in minor units — the form the provider itself uses — or in
    /// major units. Minor units win because they are exact.
    /// </summary>
    private static decimal? ReadPreviewedPaymentDue(SubscriptionRequestBody body)
    {
        var cents = body.GetDecimal(SubscriptionRequestParser.PreviewedPaymentDueInCentsNames);
        if (cents.HasValue)
        {
            return cents.Value / 100m;
        }

        return body.GetDecimal(SubscriptionRequestParser.PreviewedPaymentDueNames);
    }
}
