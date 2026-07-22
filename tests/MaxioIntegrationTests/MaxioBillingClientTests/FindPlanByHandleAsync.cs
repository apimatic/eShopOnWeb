using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class FindPlanByHandleAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReturnsTheMatchingPlanWithItsPriceInWholeCurrencyUnits()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductResponse(ProviderPayloads.ProPlanProduct));

        var plan = await BillingClientFixture.Create(_handler).FindPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Contains("eshop-pro", _handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownHandleRatherThanThrowing()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var plan = await BillingClientFixture.Create(_handler).FindPlanByHandleAsync("no-such-plan");

        Assert.Null(plan);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TreatsABlankHandleAsUnknownWithoutCallingTheProvider(string handle)
    {
        var plan = await BillingClientFixture.Create(_handler).FindPlanByHandleAsync(handle);

        Assert.Null(plan);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task DistinguishesARealFailureFromAMissingPlan()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.Unauthorized, "\"Bad credentials\"");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).FindPlanByHandleAsync("eshop-pro"));

        Assert.Equal(401, exception.StatusCode);
    }
}
