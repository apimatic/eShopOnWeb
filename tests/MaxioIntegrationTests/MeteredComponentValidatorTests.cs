using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The UC2 precondition: usage may only be recorded once the configured handle has been confirmed to
/// resolve to a metered component on the configured family.
/// </summary>
public class MeteredComponentValidatorTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();

    private static SubscriptionSettings Settings(string componentHandle = "api-call") => new()
    {
        ProductFamilyHandle = "eshop-subscribe",
        MeteredComponentHandle = componentHandle
    };

    private MeteredComponentValidator Validator(SubscriptionSettings? settings = null) =>
        new(_billingClient, Options.Create(settings ?? Settings()));

    private static MeteredComponent Component(string kind) =>
        new(3057195, "api-call", "API Calls", kind, "per_unit", 1L, "call");

    [Fact]
    public async Task AMeteredComponentValidatesAndIsReturned()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(Component(MeteredComponent.MeteredKind));

        var component = await Validator().GetValidatedComponentAsync();

        Assert.Equal(3057195, component.Id);
        Assert.True(component.IsMetered);
    }

    [Fact]
    public async Task TheConfirmedResultIsCachedSoTheProviderIsAskedOnlyOnce()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(Component(MeteredComponent.MeteredKind));

        var validator = Validator();
        await validator.GetValidatedComponentAsync();
        await validator.GetValidatedComponentAsync();

        await _billingClient.Received(1).FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AComponentOfTheWrongKindRefusesUsageAndExplainsTheRemedy()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(Component("quantity_based_component"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Validator().GetValidatedComponentAsync());

        Assert.Contains("quantity_based_component", exception.Message);
        Assert.Contains("archive it and recreate it as metered", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AHandleThatDoesNotResolveRefusesUsageAndPointsAtTheSeed()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Validator().GetValidatedComponentAsync());

        Assert.Contains("UC0", exception.Message);
    }

    [Fact]
    public async Task AnUnconfiguredHandleRefusesUsageWithoutCallingTheProvider()
    {
        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Validator(Settings(componentHandle: "")).GetValidatedComponentAsync());

        await _billingClient.DidNotReceive().FindComponentByHandleAsync(Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedValidationIsNotCachedSoCorrectingTheSeedTakesEffect()
    {
        _billingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null, Component(MeteredComponent.MeteredKind));

        var validator = Validator();

        await Assert.ThrowsAsync<BillingConfigurationException>(() => validator.GetValidatedComponentAsync());

        // The seed has since been corrected; the next call must retry rather than serve a cached failure.
        var component = await validator.GetValidatedComponentAsync();
        Assert.True(component.IsMetered);
    }
}
