using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>
/// Builds the domain service over a substituted billing client, so these tests pin the
/// orchestration rules — idempotency, transition legality, stale-preview refusal, best-effort
/// eventing — independently of any provider.
/// </summary>
public class SubscriptionServiceFixture
{
    public const string UserReference = "demouser@microsoft.com";

    public IBillingClient BillingClient { get; } = Substitute.For<IBillingClient>();

    public IPublisher Publisher { get; } = Substitute.For<IPublisher>();

    public IAppLogger<SubscriptionService> Logger { get; } = Substitute.For<IAppLogger<SubscriptionService>>();

    public ISubscriptionCatalogSettings CatalogSettings { get; } = new StubCatalogSettings();

    public SubscriptionService CreateService() =>
        new(BillingClient, Publisher, Logger, CatalogSettings);

    public static BillingCustomer Customer() =>
        new(5551212, UserReference, UserReference, "demouser", "microsoft");

    public static BillingPlan ProPlan() =>
        new(7130999, "eshop-pro", "Pro Plan", null, 299.00m, 1, "month", false);

    public static BillingPlan BasicPlan() =>
        new(7131000, "basic-plan", "Basic Plan", null, 29.00m, 1, "month", false);

    public static BillingComponent MeteredComponent() =>
        new(3062734, "api-call", "API Calls", BillingComponentKind.Metered, 0.01m, "eshop-subscribe");

    public static Subscription SubscriptionIn(SubscriptionState state, BillingPlan? plan = null) =>
        new(90210, 5551212, UserReference, plan ?? ProPlan(), state,
            DateTimeOffset.UtcNow.AddDays(10), DateTimeOffset.UtcNow.AddDays(10), false, null);

    /// <summary>Mirrors the handles the sandbox is seeded with.</summary>
    private sealed class StubCatalogSettings : ISubscriptionCatalogSettings
    {
        public string ProductFamilyHandle => "eshop-subscribe";
        public string DefaultProductHandle => "eshop-pro";
        public string AlternateProductHandle => "basic-plan";
        public string MeteredComponentHandle => "api-call";
    }
}
