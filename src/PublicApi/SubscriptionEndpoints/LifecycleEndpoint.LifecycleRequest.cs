using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary><c>Pause</c>, <c>Resume</c>, <c>Cancel</c> or <c>Reactivate</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>For <c>Cancel</c> only: <c>Immediate</c> (the default) or <c>EndOfPeriod</c>.</summary>
    public string? Timing { get; set; }

    /// <summary>Alternative to <see cref="Timing"/>: <c>true</c> defers the cancellation.</summary>
    public bool? CancelAtEndOfPeriod { get; set; }

    /// <summary>Optional reason recorded with the transition.</summary>
    public string? Reason { get; set; }

    public SubscriptionLifecycleAction ResolveAction()
        => SubscriptionRequestParser.ParseLifecycleAction(Action);

    public SubscriptionCancellationTiming ResolveCancellationTiming()
        => SubscriptionRequestParser.ParseCancellationTiming(Timing, CancelAtEndOfPeriod);

    public static LifecycleRequest From(SubscriptionRequestBody body) => new()
    {
        Action = body.GetString(SubscriptionRequestParser.ActionNames) ?? string.Empty,
        Timing = body.GetString(SubscriptionRequestParser.TimingNames),
        CancelAtEndOfPeriod = body.GetBoolean(SubscriptionRequestParser.EndOfPeriodNames),
        Reason = body.GetString(SubscriptionRequestParser.ReasonNames)
    };
}
