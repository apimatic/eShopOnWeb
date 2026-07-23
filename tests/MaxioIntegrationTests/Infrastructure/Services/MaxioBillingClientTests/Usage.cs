using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Services.MaxioBillingClientTests;

public class Usage
{
    private const string USAGES_PATH = "/subscriptions/15236915/components/handle:api-call/usages.json";
    private const string COMPONENT_PATH = "/subscriptions/15236915/components/handle:api-call.json";

    private readonly MaxioBillingClientBuilder _builder = new MaxioBillingClientBuilder();

    [Fact]
    public async Task RecordsUsageAgainstTheComponentHandleOnTheSubscription()
    {
        _builder.Stub.Respond(HttpMethod.Post, USAGES_PATH,
            MaxioPayloads.Usage(138522957, 15236915, 3057195, "api-call", "5", "Order 42"));

        var recorded = await _builder.Build().RecordUsageAsync(15236915, "api-call", 5m, "Order 42");

        Assert.Equal(138522957, recorded.Id);
        Assert.Equal(15236915, recorded.SubscriptionId);
        Assert.Equal(3057195, recorded.ComponentId);
        Assert.Equal("api-call", recorded.ComponentHandle);
        Assert.Equal(5m, recorded.Quantity);
        Assert.Equal("Order 42", recorded.Memo);
        Assert.Equal(USAGES_PATH, _builder.Stub.LastRequest.PathAndQuery);
    }

    [Fact]
    public async Task SendsTheQuantityAndMemoTheCallerAskedFor()
    {
        _builder.Stub.Respond(HttpMethod.Post, USAGES_PATH,
            MaxioPayloads.Usage(138522957, 15236915, 3057195, "api-call", "7", "Nightly batch"));

        await _builder.Build().RecordUsageAsync(15236915, "api-call", 7m, "Nightly batch");

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        var sent = body.RootElement.GetProperty("usage");
        Assert.Equal(7m, sent.GetProperty("quantity").GetDecimal());
        Assert.Equal("Nightly batch", sent.GetProperty("memo").GetString());
    }

    [Fact]
    public async Task OmitsTheMemoEntirelyWhenNoneIsGiven()
    {
        _builder.Stub.Respond(HttpMethod.Post, USAGES_PATH,
            MaxioPayloads.Usage(138522957, 15236915, 3057195, "api-call", "1", ""));

        await _builder.Build().RecordUsageAsync(15236915, "api-call", 1m, null);

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        Assert.False(body.RootElement.GetProperty("usage").TryGetProperty("memo", out _));
    }

    [Fact]
    public async Task ReadsAQuantityTheProviderReturnsAsAString()
    {
        // The specification types the returned quantity as either an integer or a string.
        _builder.Stub.Respond(HttpMethod.Post, USAGES_PATH,
            MaxioPayloads.Usage(138522957, 15236915, 3057195, "api-call", "\"1000\"", "Bulk"));

        var recorded = await _builder.Build().RecordUsageAsync(15236915, "api-call", 1000m, "Bulk");

        Assert.Equal(1000m, recorded.Quantity);
    }

    [Fact]
    public async Task SurfacesAUsageRejectionAsATypedException()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, USAGES_PATH, HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Price point: could not be found."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().RecordUsageAsync(15236915, "api-call", 1m, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Price point: could not be found.", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task ReadsTheRunningPeriodToDateBalance()
    {
        _builder.Stub.Respond(HttpMethod.Get, COMPONENT_PATH,
            MaxioPayloads.SubscriptionComponent(3057195, "api-call", "1250"));

        var balance = await _builder.Build().GetUsageBalanceAsync(15236915, "api-call");

        Assert.Equal(1250m, balance);
    }

    [Fact]
    public async Task ReportsAZeroBalanceRatherThanNoBalance()
    {
        _builder.Stub.Respond(HttpMethod.Get, COMPONENT_PATH,
            MaxioPayloads.SubscriptionComponent(3057195, "api-call", "0"));

        var balance = await _builder.Build().GetUsageBalanceAsync(15236915, "api-call");

        Assert.Equal(0m, balance);
        Assert.NotNull(balance);
    }

    [Fact]
    public async Task ReturnsNullWhenTheComponentIsNotOnTheSubscription()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, COMPONENT_PATH, HttpStatusCode.NotFound, "{}");

        Assert.Null(await _builder.Build().GetUsageBalanceAsync(15236915, "api-call"));
    }

    [Fact]
    public async Task SurfacesAFailedBalanceReadAsATypedExceptionSoTheCallerCanReportItUnavailable()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, COMPONENT_PATH, HttpStatusCode.ServiceUnavailable,
            MaxioPayloads.ErrorList("Service temporarily unavailable"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().GetUsageBalanceAsync(15236915, "api-call"));

        Assert.Equal(503, exception.StatusCode);
    }
}
