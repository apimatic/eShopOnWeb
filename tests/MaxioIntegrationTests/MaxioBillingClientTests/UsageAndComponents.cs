using System.Text.Json;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class UsageAndComponents
{
    [Fact]
    public async Task RecognisesAMeteredComponentAndReadsItsStringUnitPriceAsADecimal()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("components/lookup.json",
            MaxioJson.ComponentResponse(3062731, "api-call", "metered_component", "0.01"));

        var component = await builder.Build().GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.True(component!.IsMetered);
        Assert.Equal(3062731, component.ProviderComponentId);
        Assert.Equal("per_unit", component.PricingScheme);
        // Maxio sends "0.01" as a string; a naive parse would lose the magnitude entirely.
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Theory]
    [InlineData("quantity_based_component")]
    [InlineData("on_off_component")]
    [InlineData("prepaid_usage_component")]
    [InlineData("event_based_component")]
    public async Task DoesNotReportANonMeteredComponentAsMetered(string kind)
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("components/lookup.json",
            MaxioJson.ComponentResponse(3062731, "api-call", kind));

        var component = await builder.Build().GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.False(component!.IsMetered);
    }

    [Fact]
    public async Task ReturnsNullWhenTheComponentLivesOnADifferentProductFamily()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("components/lookup.json",
            MaxioJson.ComponentResponse(3062731, "api-call", "metered_component",
                familyHandle: "some-other-family"));

        // A component on the wrong family is not available to the plans at all.
        Assert.Null(await builder.Build().GetComponentByHandleAsync("api-call"));
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownComponentHandle()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithNotFound("components/lookup.json");

        Assert.Null(await builder.Build().GetComponentByHandleAsync("nope"));
    }

    [Fact]
    public async Task ToleratesAComponentWithNoUnitPrice()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("components/lookup.json",
            MaxioJson.ComponentResponse(3062731, "api-call", "metered_component", unitPrice: null));

        var component = await builder.Build().GetComponentByHandleAsync("api-call");

        Assert.Null(component!.UnitPrice);
    }

    [Fact]
    public async Task RecordsUsageAgainstTheComponentHandleOnTheSubscription()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("usages.json", MaxioJson.UsageResponse(900, "5"));

        var usage = await builder.Build().RecordUsageAsync(101, "api-call", 5, "five calls");

        Assert.Equal(900, usage.ProviderUsageId);
        Assert.Equal(5m, usage.Quantity);
        Assert.Equal(101, usage.SubscriptionId);
        Assert.Equal("api-call", usage.ComponentHandle);

        var request = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("subscriptions/101/components/handle:api-call/usages.json",
            request.Uri.ToString());

        using var body = JsonDocument.Parse(request.Body!);
        var payload = body.RootElement.GetProperty("usage");
        Assert.Equal(5, payload.GetProperty("quantity").GetDecimal());
        Assert.Equal("five calls", payload.GetProperty("memo").GetString());
    }

    [Fact]
    public async Task OmitsTheMemoWhenNoneWasGiven()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("usages.json", MaxioJson.UsageResponse(901, "1", memo: null));

        await builder.Build().RecordUsageAsync(101, "api-call", 1, null);

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);
        Assert.False(body.RootElement.GetProperty("usage").TryGetProperty("memo", out _));
    }

    [Fact]
    public async Task ReadsAUsageQuantityThatMaxioReturnedAsAString()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("usages.json", MaxioJson.UsageResponse(902, "\"20.5\""));

        var usage = await builder.Build().RecordUsageAsync(101, "api-call", 20.5m, null);

        Assert.Equal(20.5m, usage.Quantity);
    }

    [Fact]
    public async Task SendsFractionalQuantitiesWithoutRounding()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("usages.json", MaxioJson.UsageResponse(903, "\"2.25\""));

        await builder.Build().RecordUsageAsync(101, "api-call", 2.25m, null);

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);
        Assert.Equal(2.25m, body.RootElement.GetProperty("usage").GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task ReadsThePeriodToDateBalanceFromTheSubscriptionComponent()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101/components/handle:api-call.json",
            MaxioJson.SubscriptionComponentResponse(3062731, "api-call", 42));

        var balance = await builder.Build().GetPeriodToDateUsageAsync(101, "api-call");

        Assert.Equal(42m, balance);
    }

    [Fact]
    public async Task ReturnsNullWhenTheComponentIsNotPresentOnTheSubscription()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithNotFound("subscriptions/101/components");

        Assert.Null(await builder.Build().GetPeriodToDateUsageAsync(101, "api-call"));
    }
}
