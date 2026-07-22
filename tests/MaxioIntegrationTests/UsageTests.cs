using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC2 — recording pay-as-you-go usage and reading back the running period-to-date balance.
/// </summary>
public class UsageTests
{
    private static StubBillingServer ComponentResolved() => new StubBillingServer()
        .Get("components/lookup", BillingJson.Component(3062732, "api-call", unitPrice: "0.01"));

    [Fact]
    public async Task Records_usage_against_the_configured_metered_component()
    {
        var server = ComponentResolved()
            .Post("usages.json", BillingJson.Usage(90001, 5, "order 42"));

        var receipt = await BillingTestHarness.Build(server).RecordUsageAsync(1001, 5, "order 42");

        Assert.Equal(90001, receipt.Id);
        Assert.Equal(5m, receipt.Quantity);
        Assert.Equal("order 42", receipt.Memo);
        Assert.Equal("api-call", receipt.ComponentHandle);
        Assert.NotNull(receipt.RecordedAt);

        var posted = Assert.Single(server.RequestsFor("usages.json"));
        Assert.Contains("\"quantity\":5", posted.Body, StringComparison.Ordinal);
        Assert.Contains("\"memo\":\"order 42\"", posted.Body, StringComparison.Ordinal);

        // The numeric component id resolved from the handle is what the usage is filed against.
        Assert.Contains("3062732", posted.Path, StringComparison.Ordinal);
        Assert.Contains("1001", posted.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_a_quantity_the_provider_echoes_back_as_a_string()
    {
        // The provider's usage model echoes quantity as either a number or a string.
        var server = ComponentResolved()
            .Post("usages.json", """
            {"usage":{"id":90002,"quantity":"7","memo":null,"created_at":"2026-07-22T10:00:00-04:00","component_handle":"api-call"}}
            """);

        var receipt = await BillingTestHarness.Build(server).RecordUsageAsync(1001, 7, null);

        Assert.Equal(7m, receipt.Quantity);
    }

    [Fact]
    public async Task Surfaces_a_rejected_usage_report_as_a_typed_billing_exception()
    {
        var server = ComponentResolved()
            .Post("usages.json", BillingJson.Errors("Subscription must be in an active state."), HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingTestHarness.Build(server).RecordUsageAsync(1001, 1, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("active state", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_record_usage_when_the_component_is_not_metered()
    {
        var server = new StubBillingServer()
            .Get("components/lookup", BillingJson.Component(3062732, "api-call", kind: "on_off_component"));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).RecordUsageAsync(1001, 1, null));

        // Nothing was sent to the usage endpoint: the precondition failed first.
        Assert.Empty(server.RequestsFor("usages.json"));
    }

    [Fact]
    public async Task Reads_the_running_period_to_date_unit_balance()
    {
        var server = ComponentResolved()
            .Get("/components/3062732.json", BillingJson.SubscriptionComponent(3062732, 137));

        var balance = await BillingTestHarness.Build(server).GetPeriodToDateUsageAsync(1001);

        Assert.Equal(137, balance);
    }

    [Fact]
    public async Task Reports_no_balance_when_the_subscription_carries_none_for_the_component()
    {
        var server = ComponentResolved()
            .Get("/components/3062732.json", BillingJson.NotFound(), HttpStatusCode.NotFound);

        var balance = await BillingTestHarness.Build(server).GetPeriodToDateUsageAsync(1001);

        Assert.Null(balance);
    }
}
