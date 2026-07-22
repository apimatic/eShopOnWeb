using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class FindComponentByHandleAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task RecognisesAMeteredComponentAndPricesItInWholeCurrencyUnits()
    {
        _handler.RespondWithJson(ProviderPayloads.ComponentResponse(ProviderPayloads.MeteredComponent));

        var component = await BillingClientFixture.Create(_handler)
            .FindComponentByHandleAsync(BillingClientFixture.ComponentHandle);

        Assert.NotNull(component);
        Assert.True(component.IsMetered);
        Assert.Equal(BillingComponentKind.Metered, component.Kind);
        // 1 cent per unit is $0.01 — the magnitude the pay-as-you-go demo depends on.
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal("eshop-subscribe", component.ProductFamilyHandle);
    }

    [Fact]
    public async Task DoesNotReportAQuantityBasedComponentAsMetered()
    {
        _handler.RespondWithJson(ProviderPayloads.ComponentResponse(ProviderPayloads.QuantityBasedComponent));

        var component = await BillingClientFixture.Create(_handler)
            .FindComponentByHandleAsync(BillingClientFixture.ComponentHandle);

        Assert.NotNull(component);
        Assert.False(component.IsMetered);
        Assert.Equal(BillingComponentKind.QuantityBased, component.Kind);
        Assert.Equal(5.00m, component.UnitPrice);
    }

    [Fact]
    public async Task ReturnsNullWhenNoComponentCarriesTheHandle()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var component = await BillingClientFixture.Create(_handler)
            .FindComponentByHandleAsync("not-seeded");

        Assert.Null(component);
    }
}
