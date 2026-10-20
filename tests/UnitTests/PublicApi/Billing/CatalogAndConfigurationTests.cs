using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

public class CatalogAndConfigurationTests
{
    private static readonly BillingUser DemoUser = new("demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        var fake = new FakeMaxio();
        var (service, db) = fake.NewScope();

        var catalog = await service.GetPlansAsync(default);
        db.Dispose();

        Assert.False(catalog.Truncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        Assert.Equal(29900, catalog.Plans[0].PriceInCents);
        Assert.Equal("month", catalog.Plans[0].IntervalUnit);
        var request = fake.Stub.Requests.Single();
        Assert.Contains("per_page=200", request.Query);
        Assert.Contains("include_archived=false", request.Query);
    }

    [Fact]
    public async Task PageCapMarksTheCatalogueTruncatedAndAMissingPlanIsNotReportedAsInvalid()
    {
        var fake = new FakeMaxio { Policy = new MaxioCallPolicy { PlanPageSize = 2, MaxPlanPages = 1, MaxRetries = 0 } };
        var (service, db) = fake.NewScope();

        var catalog = await service.GetPlansAsync(default);
        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(DemoUser, "some-later-plan", default));
        db.Dispose();

        Assert.True(catalog.Truncated);
        Assert.Equal(503, ex.HttpStatusCode);
    }

    [Fact]
    public async Task PlanNotOfferedIsRejectedBeforeAnyClaimOrWrite()
    {
        var fake = new FakeMaxio();
        var (service, db) = fake.NewScope();

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(DemoUser, "not-a-plan", default));
        db.Dispose();

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Get, "/customers/lookup.json"));
    }

    [Fact]
    public async Task RejectedCredentialsAreNotPassedThroughAsTheCallersFault()
    {
        var fake = new FakeMaxio();
        fake.Stub.OnJson(HttpMethod.Get, $"/product_families/handle:{FakeMaxio.Family}/products.json", HttpStatusCode.Unauthorized, "");
        var (service, db) = fake.NewScope();

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync(default));
        db.Dispose();

        Assert.Equal(502, ex.HttpStatusCode);
        Assert.Equal(401, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task CustomerLookupFailureIsNotTreatedAsAbsence()
    {
        var fake = new FakeMaxio();
        fake.Stub.OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.InternalServerError, "{}");
        var (service, db) = fake.NewScope();

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(DemoUser, "eshop-pro", default));
        db.Dispose();

        Assert.Equal(502, ex.HttpStatusCode);
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Post, "/customers.json")); // no create on an unreadable lookup
    }

    [Fact]
    public async Task MySubscriptionsNeverCreatesACustomer()
    {
        var fake = new FakeMaxio();
        var (service, db) = fake.NewScope();

        var result = await service.GetMySubscriptionsAsync(DemoUser, default);
        db.Dispose();

        Assert.False(result.HasBillingAccount);
        Assert.Empty(result.Subscriptions);
        Assert.Equal(0, fake.Stub.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task MySubscriptionsReportsPlanPriceStateAndNextBillingDate()
    {
        var fake = new FakeMaxio();
        var (s1, db1) = fake.NewScope();
        await s1.SubscribeAsync(DemoUser, "eshop-pro", default);
        db1.Dispose();

        var (s2, db2) = fake.NewScope();
        var result = await s2.GetMySubscriptionsAsync(DemoUser, default);
        db2.Dispose();

        var subscription = Assert.Single(result.Subscriptions);
        Assert.True(result.HasBillingAccount);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("active", subscription.State);
        Assert.NotNull(subscription.NextBillingAt);
    }

    [Fact]
    public void SettingsValidationNamesTheMissingKeyAndNeverTheValue()
    {
        var validator = new MaxioSettingsValidator();

        var missing = validator.Validate(null, new MaxioSettings { ApiKey = " ", Subdomain = "site", ProductFamilyHandle = "fam" });
        Assert.True(missing.Failed);
        Assert.Contains("Maxio:ApiKey", missing.FailureMessage);

        var noSite = validator.Validate(null, new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam" });
        Assert.True(noSite.Failed);
        Assert.Contains("Maxio:Subdomain", noSite.FailureMessage);

        var baseUrlInstead = validator.Validate(null, new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam", BaseUrl = "https://maxio.test.invalid" });
        Assert.True(baseUrlInstead.Succeeded);

        var badBaseUrl = validator.Validate(null, new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam", BaseUrl = "not a url" });
        Assert.True(badBaseUrl.Failed);
        Assert.Contains("Maxio:BaseUrl", badBaseUrl.FailureMessage);
    }

    [Fact]
    public async Task BaseUrlOverrideIsUsedVerbatim()
    {
        var fake = new FakeMaxio();
        var (service, db) = fake.NewScope();
        await service.GetPlansAsync(default);
        db.Dispose();

        // FakeMaxio sets Maxio:BaseUrl = https://maxio.test.invalid (no Maxio:Subdomain at all).
        var sent = Assert.Single(fake.Stub.Sent);
        Assert.Equal("maxio.test.invalid", sent.Uri.Host);
        Assert.StartsWith("/product_families/", sent.Uri.AbsolutePath);
        Assert.Equal("Basic", sent.AuthScheme);
    }

    [Fact]
    public async Task CreateRequestsCarryTheReferencesAndRemittanceCollection()
    {
        var fake = new FakeMaxio();
        var (service, db) = fake.NewScope();
        var result = await service.SubscribeAsync(DemoUser, "eshop-pro", default);
        db.Dispose();

        var customerBody = fake.Stub.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/customers.json").Body!;
        Assert.Contains("\"reference\":\"eshop:demouser@microsoft.com\"", customerBody);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerBody);

        var subscriptionBody = fake.Stub.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json").Body!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);
        Assert.Contains($"\"reference\":\"{result.SubscriptionReference}\"", subscriptionBody);
    }

    [Fact]
    public void NamesAreDerivedFromTheEmail()
    {
        Assert.Equal(("Demouser", "Customer"), SubscriptionService.NamesFor("demouser@microsoft.com"));
        Assert.Equal(("Jane", "Doe"), SubscriptionService.NamesFor("jane.doe@example.com"));
    }
}
