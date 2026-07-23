using System.Globalization;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Usage
{
    private readonly RecordingHttpMessageHandler _handler = new();

    private static string UsagesPath =>
        $"/subscriptions/{MaxioResponses.SubscriptionId}/components/{MaxioResponses.ComponentId}/usages.json";

    private static string SubscriptionComponentPath =>
        $"/subscriptions/{MaxioResponses.SubscriptionId}/components/{MaxioResponses.ComponentId}.json";

    private void ArrangeFamilyAndComponents(string components = MaxioResponses.MeteredComponents)
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ComponentsPath, components);
    }

    [Fact]
    public async Task ResolvesTheConfiguredMeteredComponentFromItsHandle()
    {
        ArrangeFamilyAndComponents();

        var component = await TestBillingClientFactory.Create(_handler).GetUsageComponentAsync();

        Assert.Equal(MaxioResponses.ComponentId, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
    }

    /// <summary>
    /// Component prices arrive as a decimal string in major units, unlike the *_in_cents fields.
    /// </summary>
    [Fact]
    public async Task ReadsThePerUnitPriceAsMajorUnits()
    {
        ArrangeFamilyAndComponents();

        var component = await TestBillingClientFactory.Create(_handler).GetUsageComponentAsync();

        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.Equal("api call", component.UnitName);
    }

    /// <summary>
    /// Under a comma-decimal locale a culture-sensitive parse would read "0.01" as 1, inflating
    /// every metered charge a hundredfold.
    /// </summary>
    [Fact]
    public async Task ParsesThePerUnitPriceIndependentlyOfTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            ArrangeFamilyAndComponents();

            var component = await TestBillingClientFactory.Create(_handler).GetUsageComponentAsync();

            Assert.Equal(0.01m, component.UnitPrice);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// A component's kind cannot be converted in place, so a non-metered component must be refused
    /// outright rather than producing a confusing failure at usage time.
    /// </summary>
    [Fact]
    public async Task RefusesAConfiguredComponentThatIsNotMetered()
    {
        ArrangeFamilyAndComponents(MaxioResponses.QuantityBasedComponents);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetUsageComponentAsync());
        Assert.Contains("quantity_based_component", exception.Message);
        Assert.Contains("not metered", exception.Message);
    }

    [Fact]
    public async Task RefusesAConfiguredComponentHandleThatDoesNotResolve()
    {
        ArrangeFamilyAndComponents(MaxioResponses.EmptyArray);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetUsageComponentAsync());
        Assert.Contains("api-call", exception.Message);
    }

    [Fact]
    public async Task RefusesToResolveAUsageComponentWhenNoHandleIsConfigured()
    {
        var settings = TestBillingClientFactory.Settings(s => s.MeteredComponentHandle = string.Empty);
        var client = TestBillingClientFactory.Create(_handler, settings);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetUsageComponentAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task ExcludesArchivedComponentsWhenResolvingByHandle()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ComponentsPath, """
                [{"component":{"id":1,"name":"API Calls","handle":"api-call","pricing_scheme":"per_unit",
                  "unit_name":"api call","unit_price":"0.01","kind":"metered_component","archived":true}}]
                """);

        var component = await TestBillingClientFactory.Create(_handler).FindComponentByHandleAsync("api-call");

        Assert.Null(component);
    }

    [Fact]
    public async Task RecordsUsageAndReturnsTheProviderReceiptId()
    {
        _handler.RespondJson(HttpMethod.Post, UsagesPath, MaxioResponses.Usage);

        var usageId = await TestBillingClientFactory.Create(_handler).RecordUsageAsync(
            new RecordUsageRequest(MaxioResponses.SubscriptionId, MaxioResponses.ComponentId, 5, "probe usage"));

        Assert.Equal(3633939705, usageId);
    }

    [Fact]
    public async Task SendsTheQuantityAndMemoToTheProvider()
    {
        _handler.RespondJson(HttpMethod.Post, UsagesPath, MaxioResponses.Usage);

        await TestBillingClientFactory.Create(_handler).RecordUsageAsync(
            new RecordUsageRequest(MaxioResponses.SubscriptionId, MaxioResponses.ComponentId, 5, "order 42"));

        var body = Assert.Single(_handler.Requests).Body!;
        Assert.Contains("\"quantity\":5", body);
        Assert.Contains("\"memo\":\"order 42\"", body);
    }

    [Fact]
    public async Task ReadsTheRunningPeriodToDateTotal()
    {
        _handler.RespondJson(HttpMethod.Get, SubscriptionComponentPath, MaxioResponses.SubscriptionComponent(5));

        var total = await TestBillingClientFactory.Create(_handler)
            .GetPeriodToDateUnitsAsync(MaxioResponses.SubscriptionId, MaxioResponses.ComponentId);

        Assert.Equal(5, total);
    }

    /// <summary>A component that has never accrued usage on this subscription answers 404.</summary>
    [Fact]
    public async Task ReturnsNullPeriodToDateTotalWhenNoUsageHasEverBeenRecorded()
    {
        _handler.RespondStatus(HttpMethod.Get, SubscriptionComponentPath, HttpStatusCode.NotFound);

        var total = await TestBillingClientFactory.Create(_handler)
            .GetPeriodToDateUnitsAsync(MaxioResponses.SubscriptionId, MaxioResponses.ComponentId);

        Assert.Null(total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsAZeroOrNegativeQuantityBeforeAnyProviderCall(int quantity)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new RecordUsageRequest(MaxioResponses.SubscriptionId, MaxioResponses.ComponentId, quantity));

        Assert.Empty(_handler.Requests);
    }

    /// <summary>Estimating the metered charge must not lose the cents-scale magnitude.</summary>
    [Fact]
    public void EstimatesThePeriodToDateChargeFromUnitsAndUnitPrice()
    {
        var result = new UsageRecordResult(1, MaxioResponses.SubscriptionId, "api-call", 5, null,
            periodToDateUnits: 250, unitPrice: 0.01m);

        Assert.True(result.PeriodToDateAvailable);
        Assert.Equal(2.50m, result.PeriodToDateEstimatedCharge);
    }

    [Fact]
    public void ReportsThePeriodToDateTotalAsUnavailableRatherThanZeroWhenTheReadBackFailed()
    {
        var result = new UsageRecordResult(1, MaxioResponses.SubscriptionId, "api-call", 5, null,
            periodToDateUnits: null, unitPrice: 0.01m);

        Assert.False(result.PeriodToDateAvailable);
        Assert.Null(result.PeriodToDateUnits);
        Assert.Null(result.PeriodToDateEstimatedCharge);
    }
}
