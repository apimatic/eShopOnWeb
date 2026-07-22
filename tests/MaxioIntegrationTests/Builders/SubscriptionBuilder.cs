using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Builds domain subscriptions for the seam tests.
/// </summary>
public class SubscriptionBuilder
{
    public const string UserName = "demouser@microsoft.com";

    private int _id = 15236915;
    private string _planHandle = MaxioClientBuilder.DefaultProductHandle;
    private SubscriptionState _state = SubscriptionState.Active;
    private bool _cancelAtEndOfPeriod;

    public SubscriptionBuilder WithId(int id)
    {
        _id = id;
        return this;
    }

    public SubscriptionBuilder OnPlan(string planHandle)
    {
        _planHandle = planHandle;
        return this;
    }

    public SubscriptionBuilder InState(SubscriptionState state)
    {
        _state = state;
        return this;
    }

    public SubscriptionBuilder CancellingAtPeriodEnd()
    {
        _cancelAtEndOfPeriod = true;
        return this;
    }

    public Subscription Build() => new(
        _id,
        UserName,
        88001,
        _planHandle,
        _planHandle == MaxioClientBuilder.DefaultProductHandle ? "Pro Plan" : "Basic Plan",
        _planHandle == MaxioClientBuilder.DefaultProductHandle ? 29900 : 2900,
        _state,
        new DateTimeOffset(2026, 8, 22, 14, 48, 10, TimeSpan.FromHours(-5)),
        new DateTimeOffset(2026, 7, 22, 14, 48, 12, TimeSpan.FromHours(-5)),
        _cancelAtEndOfPeriod,
        null,
        null);
}
