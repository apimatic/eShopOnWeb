using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The UC2 startup check. It must report what it finds and, above all, never prevent the host from
/// starting — eShopOnWeb's catalog, basket and order flows cannot depend on the billing provider being
/// reachable.
/// </summary>
public class MaxioStartupValidatorTests
{
    private static (MaxioStartupValidator Validator, FakeBillingClient Billing) Build(MaxioSettings settings)
    {
        var billing = new FakeBillingClient();

        var services = new ServiceCollection();
        services.AddScoped(typeof(IAppLogger<>), typeof(NullAppLogger<>));
        services.AddScoped<IBillingClient>(_ => billing);

        var provider = services.BuildServiceProvider();

        return (new MaxioStartupValidator(
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(settings)), billing);
    }

    [Fact]
    public async Task StartAsync_VerifiesTheCatalogAndTheMeteredComponent()
    {
        var (validator, billing) = Build(BillingTestContext.DefaultSettings());
        billing.Plans.Add(new BillingPlan(1, "eshop-pro", "Pro Plan", null, 299m, 1, "month", false, false));
        billing.Component = new MeteredComponent(
            MaxioPayloads.ComponentId, "api-call", "API Calls", "metered_component", true, 0.01m, "per_unit");

        await validator.StartAsync(CancellationToken.None);

        Assert.Contains("ListPlansAsync", billing.Calls);
        Assert.Contains("FindComponentByHandleAsync:api-call", billing.Calls);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenTheComponentIsOfTheWrongKind()
    {
        var (validator, billing) = Build(BillingTestContext.DefaultSettings());
        billing.Component = new MeteredComponent(
            1, "api-call", "API Calls", "quantity_based_component", false, 0.01m, "per_unit");

        // Reported, never fatal.
        await validator.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenTheComponentIsMissing()
    {
        var (validator, billing) = Build(BillingTestContext.DefaultSettings());
        billing.Component = null;

        await validator.StartAsync(CancellationToken.None);

        Assert.Contains("FindComponentByHandleAsync:api-call", billing.Calls);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenTheProviderIsUnreachable()
    {
        var (validator, billing) = Build(BillingTestContext.DefaultSettings());
        billing.Component = null;
        billing.PeriodToDateFailure = new BillingProviderException("unreachable");

        // A billing outage must never stop eShopOnWeb from starting.
        await validator.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_SkipsTheCatalogCheck_WhenBillingIsNotConfigured()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.ApiKey = string.Empty;

        var (validator, billing) = Build(settings);

        await validator.StartAsync(CancellationToken.None);

        // Nothing is attempted against a provider we have no credentials for.
        Assert.Empty(billing.Calls);
    }

    [Fact]
    public async Task StopAsync_Completes()
    {
        var (validator, _) = Build(BillingTestContext.DefaultSettings());

        await validator.StopAsync(CancellationToken.None);
    }
}
