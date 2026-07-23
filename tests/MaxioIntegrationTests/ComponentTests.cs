using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Resolving the metered component. UC2 refuses to record usage unless the configured handle
/// resolves to a metered component on the configured family, so these are the guard rails.
/// </summary>
public class ComponentTests
{
    [Fact]
    public async Task AMeteredComponentResolvesWithItsKindAndUnitPriceInCents()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.MeteredComponent);

        var component = await client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(3057195, component!.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.Equal("metered_component", component.Kind);
        Assert.True(component.IsMetered);
        Assert.Equal(1L, component.PricePerUnitInCents);
        Assert.Equal(0.01m, component.PricePerUnit);
    }

    [Fact]
    public async Task AUnitPriceReportedOnlyAsAStringIsStillReadAsCents()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.MeteredComponentPriceAsString);

        var component = await client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(1L, component!.PricePerUnitInCents);
        Assert.Equal(0.01m, component.PricePerUnit);
    }

    [Fact]
    public async Task AComponentOfTheWrongKindIsReportedAsNotMetered()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.QuantityBasedComponent);

        var component = await client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task AComponentOnADifferentProductFamilyIsAConfigurationFailure()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.ForeignFamilyComponent);

        // Such a component is not available to this integration's subscriptions, so treating it as a
        // match would make usage silently fail later.
        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.FindComponentByHandleAsync("api-call"));

        Assert.Contains("some-other-family", exception.Message);
    }

    [Fact]
    public async Task AnUnknownComponentHandleResolvesToNull()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.NotFound, ProviderPayloads.NotFoundError);

        Assert.Null(await client.FindComponentByHandleAsync("no-such-component"));
    }

    [Fact]
    public async Task AnEmptyComponentHandleDoesNotCallTheProvider()
    {
        var (client, handler) = BillingClientFixture.Create();

        Assert.Null(await client.FindComponentByHandleAsync(""));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AProviderOutageResolvingAComponentSurfacesAsATypedException()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.FindComponentByHandleAsync("api-call"));

        Assert.Equal("FindComponentByHandle", exception.Operation);
    }
}
