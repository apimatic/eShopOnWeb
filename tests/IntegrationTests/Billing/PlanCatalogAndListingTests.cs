#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Microsoft.eShopWeb.IntegrationTests.Billing.MaxioTestHarness;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class PlanCatalogAndListingTests
{
    [Fact]
    public async Task ListsNonArchivedPlansOfTheConfiguredFamily()
    {
        var harness = new MaxioTestHarness();
        harness.Stub.On(HttpMethod.Get, ProductsPath, HttpStatusCode.OK,
            """[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},""" +
            """{"product":{"handle":"old-plan","name":"Old","price_in_cents":100,"archived_at":"2024-01-01T00:00:00Z"}}]""");

        var catalog = await harness.NewService().ListPlansAsync();

        var plan = Assert.Single(catalog.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(catalog.Truncated);
    }

    [Fact]
    public async Task CatalogLargerThanTheCapIsReportedAsTruncated()
    {
        var harness = new MaxioTestHarness();
        var fullPage = MaxioStub.ProductsJson(Enumerable.Range(0, MaxioBillingLimits.PlanPageSize)
            .Select(i => ($"plan-{i}", $"Plan {i}", 100L)).ToArray());
        harness.Stub.On(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, fullPage);

        var catalog = await harness.NewService().ListPlansAsync();

        Assert.True(catalog.Truncated);
        Assert.Equal(MaxioBillingLimits.MaxPlanPages, harness.Stub.Count(HttpMethod.Get, ProductsPath));
    }

    [Fact]
    public async Task UnknownProductFamilyIsAConfigurationError()
    {
        var harness = new MaxioTestHarness();
        harness.Stub.On(HttpMethod.Get, ProductsPath, HttpStatusCode.NotFound, "\"Not found\"");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => harness.NewService().ListPlansAsync());

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
    }

    [Fact]
    public async Task UnreachableProviderIsReportedAsUnavailableWithASafeMessage()
    {
        var harness = new MaxioTestHarness();
        harness.Stub.On(HttpMethod.Get, ProductsPath, (_, _) => throw new HttpRequestException("no route to host"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => harness.NewService().ListPlansAsync());

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.DoesNotContain("maxio.test", ex.Message);
    }

    [Fact]
    public async Task UserWithoutACustomerHasNoSubscriptionsAndNothingIsCreated()
    {
        var harness = new MaxioTestHarness();
        harness.Stub.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{}");

        var subscriptions = await harness.NewService().ListSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
        Assert.Equal(0, harness.Stub.Count(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task ListsTheCallersSubscriptionsFromMaxio()
    {
        var harness = new MaxioTestHarness();
        harness.Stub
            .On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioStub.CustomerJson(501, MaxioCustomerProvisioner.CustomerReferenceFor(UserName)))
            .On(HttpMethod.Get, CustomerSubscriptionsPath(501), HttpStatusCode.OK,
                MaxioStub.SubscriptionListJson(new[] { MaxioStub.SubscriptionJson(7001, "eshop-sub-a", 501, "eshop-pro") }));

        var subscriptions = await harness.NewService().ListSubscriptionsAsync(UserName);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(7001, subscription.SubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal("USD", subscription.Currency);
        Assert.NotNull(subscription.NextBillingAt);
    }

    [Fact]
    public async Task BaseUrlIsDerivedFromTheSubdomainWhenNotOverridden()
    {
        var stub = new MaxioStub();
        string? host = null;
        stub.On(HttpMethod.Get, ProductsPath, (request, _) =>
        {
            host = request.RequestUri!.Host;
            return Task.FromResult(MaxioStub.Json(HttpStatusCode.OK, "[]"));
        });
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "acme-sandbox", ProductFamilyHandle = FamilyHandle };
        var client = new MaxioAdvancedBillingClient(new HttpClient(stub),
            MaxioBillingServiceCollectionExtensions.BuildClientOptions(settings, NullLoggerFactory.Instance, TimeProvider.System));

        await new MaxioPlanCatalog(client, settings, NullLogger<MaxioPlanCatalog>.Instance).ReadAsync(default);

        Assert.Equal("acme-sandbox.chargify.com", host);
    }

    [Fact]
    public void SettingsValidationNamesEveryMissingKeyWithoutEchoingValues()
    {
        var problems = new MaxioSettings { ApiKey = " ", BaseUrl = "not a url", PaymentCollectionMethod = "cash" }.Validate();

        Assert.Contains(problems, p => p.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, p => p.Contains("Maxio:ProductFamilyHandle"));
        Assert.Contains(problems, p => p.Contains("Maxio:BaseUrl"));
        Assert.Contains(problems, p => p.Contains("Maxio:PaymentCollectionMethod"));
        Assert.DoesNotContain(problems, p => p.Contains("not a url") || p.Contains("cash"));
    }

    [Fact]
    public void SubdomainIsOptionalWhenBaseUrlIsSet()
    {
        var problems = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://example.test" }.Validate();

        Assert.Empty(problems);
    }
}
