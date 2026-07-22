using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Builds a <see cref="SubscriptionService"/> over a substituted billing client, so the seam's own
/// rules can be exercised independently of the provider.
/// </summary>
public class SubscriptionServiceBuilder
{
    public IBillingClient BillingClient { get; } = Substitute.For<IBillingClient>();

    public IPublisher Publisher { get; } = Substitute.For<IPublisher>();

    public IAppLogger<SubscriptionService> Logger { get; } = Substitute.For<IAppLogger<SubscriptionService>>();

    public MaxioSettings Settings { get; } = new()
    {
        ProductFamilyHandle = MaxioClientBuilder.ProductFamilyHandle,
        DefaultProductHandle = MaxioClientBuilder.DefaultProductHandle,
        AlternateProductHandle = MaxioClientBuilder.AlternateProductHandle,
        MeteredComponentHandle = MaxioClientBuilder.MeteredComponentHandle
    };

    /// <summary>
    /// Makes the configured plan handles resolve, which most operations require up front.
    /// </summary>
    public SubscriptionServiceBuilder WithResolvablePlans()
    {
        BillingClient.FindPlanAsync(MaxioClientBuilder.DefaultProductHandle, Arg.Any<CancellationToken>())
            .Returns(new BillingPlan(7130995, MaxioClientBuilder.DefaultProductHandle, "Pro Plan", null,
                29900, 1, "month", false));
        BillingClient.FindPlanAsync(MaxioClientBuilder.AlternateProductHandle, Arg.Any<CancellationToken>())
            .Returns(new BillingPlan(7130996, MaxioClientBuilder.AlternateProductHandle, "Basic Plan", null,
                2900, 1, "month", false));
        return this;
    }

    /// <summary>
    /// Makes the configured usage component resolve as a metered component.
    /// </summary>
    public SubscriptionServiceBuilder WithMeteredComponent()
    {
        BillingClient.FindMeteredComponentAsync(MaxioClientBuilder.MeteredComponentHandle,
                Arg.Any<CancellationToken>())
            .Returns(new MeteredComponent(3062732, MaxioClientBuilder.MeteredComponentHandle, "API Calls",
                MeteredComponent.MeteredKind, "api call", "per_unit", 0.01m));
        return this;
    }

    public SubscriptionService Build() => new(BillingClient, Publisher, Logger, Settings);
}
