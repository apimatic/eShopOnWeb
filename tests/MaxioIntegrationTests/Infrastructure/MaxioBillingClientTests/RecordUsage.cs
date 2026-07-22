using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class RecordUsage
{
    private const string UsagePath = "subscriptions/15236915/components/handle:api-call/usages.json";
    private const string ComponentPath = "subscriptions/15236915/components/handle:api-call.json";

    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task PostsTheQuantityAndMemoAgainstTheComponentHandle()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, UsagePath, HttpStatusCode.OK, MaxioPayloads.Usage);

        var record = await _builder.Build().RecordUsageAsync(15236915, "api-call", 3, "Order 42 placed");

        Assert.Equal(138522957, record.Id);
        Assert.Equal(3, record.Quantity);
        Assert.Equal("Order 42 placed", record.Memo);
        Assert.Equal(15236915, record.SubscriptionId);
        Assert.Equal(MaxioPayloads.ComponentId, record.ComponentId);
        Assert.Equal("api-call", record.ComponentHandle);

        // The component is addressed by handle, not by a numeric id that a reseed would invalidate.
        var request = Assert.Single(_builder.Handler.Requests);
        Assert.Equal(UsagePath, request.PathAndQuery);
        Assert.Contains("\"quantity\":3", request.Body);
        Assert.Contains("\"memo\":\"Order 42 placed\"", request.Body);
    }

    [Fact]
    public async Task OmitsTheMemoWhenNoneIsSupplied()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, UsagePath, HttpStatusCode.OK, MaxioPayloads.Usage);

        await _builder.Build().RecordUsageAsync(15236915, "api-call", 1, null);

        Assert.DoesNotContain("memo", Assert.Single(_builder.Handler.Requests).Body);
    }

    [Fact]
    public async Task ReadsTheRunningPeriodToDateBalance()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, ComponentPath, HttpStatusCode.OK,
            MaxioPayloads.SubscriptionComponent("42"));

        var balance = await _builder.Build().GetUsageBalanceAsync(15236915, "api-call");

        Assert.Equal(42m, balance);
    }

    [Fact]
    public async Task ReadsAFractionalBalanceSuppliedAsAString()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, ComponentPath, HttpStatusCode.OK,
            MaxioPayloads.SubscriptionComponent("\"12.5\""));

        var balance = await _builder.Build().GetUsageBalanceAsync(15236915, "api-call");

        // Maxio types several numeric fields as "integer or string"; both must read back the same.
        Assert.Equal(12.5m, balance);
    }

    [Fact]
    public async Task ReturnsNullWhenTheComponentIsNotOnTheSubscription()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, ComponentPath, HttpStatusCode.NotFound, string.Empty);

        var balance = await _builder.Build().GetUsageBalanceAsync(15236915, "api-call");

        Assert.Null(balance);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfTheUsageReport()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, UsagePath, HttpStatusCode.UnprocessableEntity,
            """{"errors":["Price point: could not be found."]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().RecordUsageAsync(15236915, "api-call", 1, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Price point: could not be found.", exception.Errors);
    }
}
