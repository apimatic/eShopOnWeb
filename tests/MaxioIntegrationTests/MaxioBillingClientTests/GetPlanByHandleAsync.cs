using System.Net;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class GetPlanByHandleAsync
{
    [Fact]
    public async Task ReadsThePlanByItsDurableHandle()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products/handle/eshop-pro.json",
            MaxioJson.ProductResponse(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 29900));

        var plan = await builder.Build().GetPlanByHandleAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan!.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Contains("products/handle/eshop-pro.json", builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownHandleSoCallersCanReportAConfigurationError()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithNotFound("products/handle/gone.json");

        var plan = await builder.Build().GetPlanByHandleAsync("gone");

        Assert.Null(plan);
    }

    [Fact]
    public async Task ReturnsNullWithoutCallingTheProviderForAnEmptyHandle()
    {
        var builder = new MaxioClientBuilder();

        var plan = await builder.Build().GetPlanByHandleAsync("");

        Assert.Null(plan);
        Assert.Empty(builder.Handler.Requests);
    }

    [Fact]
    public async Task EscapesTheHandleSoItCannotAlterTheRequestPath()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("products/handle", HttpStatusCode.NotFound, "{}");

        await builder.Build().GetPlanByHandleAsync("a/../b");

        Assert.DoesNotContain("a/../b", builder.Handler.LastRequest.Uri.ToString());
        Assert.Contains("a%2F..%2Fb", builder.Handler.LastRequest.Uri.ToString());
    }
}
