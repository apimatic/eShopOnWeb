using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Seam;

/// <summary>
/// UC2's precondition: usage is only ever reported against a component the provider confirms is
/// metered.
/// </summary>
public class MeteredComponentValidatorTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly MeteredComponentValidator _validator = new();

    [Fact]
    public async Task AcceptsAComponentTheProviderReportsAsMetered()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(new MeteredComponent(1, "api-call", "API Calls", "metered_component") { IsMetered = true });

        await _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call");

        await _billingClient.Received(1).FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesWhenTheConfiguredHandleDoesNotResolve()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call"));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task RefusesWhenTheComponentIsTheWrongKind()
    {
        // UC0's classic mis-seed: created as quantity-based instead of metered.
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(new MeteredComponent(1, "api-call", "API Calls", "quantity_based_component")
            {
                IsMetered = false
            });

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call"));

        Assert.Contains("quantity_based_component", exception.Message);
        Assert.Contains("archive it and recreate it as metered", exception.Message);
    }

    [Fact]
    public async Task ValidatesOnceAndCachesTheResultForTheProcess()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(new MeteredComponent(1, "api-call", "API Calls", "metered_component") { IsMetered = true });

        await _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call");
        await _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call");
        await _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call");

        // The check costs one provider call per process, not one per usage report.
        await _billingClient.Received(1).FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidatesWhenTheConfiguredHandleChanges()
    {
        _billingClient.FindComponentByHandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new MeteredComponent(1, callInfo.Arg<string>(), "Component", "metered_component")
            {
                IsMetered = true
            });

        await _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call");
        await _validator.EnsureComponentIsMeteredAsync(_billingClient, "other-component");

        await _billingClient.Received(1).FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>());
        await _billingClient.Received(1).FindComponentByHandleAsync("other-component", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCacheAFailedValidation()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call"));
        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _validator.EnsureComponentIsMeteredAsync(_billingClient, "api-call"));

        // A corrected seed must be picked up without restarting the application.
        await _billingClient.Received(2).FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>());
    }
}
