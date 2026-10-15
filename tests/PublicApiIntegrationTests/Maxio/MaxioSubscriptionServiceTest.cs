using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// Drives the real service, the real SDK client and a real (in-memory) EF store; only the Maxio transport is faked.
/// </summary>
[TestClass]
public class MaxioSubscriptionServiceTest
{
    private const string User = "demouser@microsoft.com";
    private const string Email = "demouser@microsoft.com";

    private FakeMaxioHandler _fake = null!;
    private InMemoryDatabaseRoot _root = null!;
    private MaxioAdvancedBillingClient _client = null!;

    [TestInitialize]
    public void Init()
    {
        _fake = new FakeMaxioHandler();
        _root = new InMemoryDatabaseRoot();
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "placeholder", Password = "x" }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.invalid";
        _client = new MaxioAdvancedBillingClient(new HttpClient(_fake), options);
    }

    // A fresh service + DbContext per call: separate scopes sharing one store, like separate requests.
    private (MaxioSubscriptionService Service, CatalogContext Db) NewScope()
    {
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase("claims", _root).Options);
        var settings = Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" });
        var site = new MaxioSiteInfo(_client);
        return (new MaxioSubscriptionService(_client, site, db, settings, NullLogger<MaxioSubscriptionService>.Instance), db);
    }

    private async Task<string?> ClaimState(string prefix)
    {
        var (_, db) = NewScope();
        return (await db.MaxioClaims.ToListAsync()).FirstOrDefault(c => c.Reference.StartsWith(prefix))?.State;
    }

    [TestMethod]
    public async Task ListsOnlyActivePlansOfTheFamily()
    {
        var plans = await NewScope().Service.ListPlansAsync();

        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, plans.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(299m, plans.Plans.Single(p => p.Handle == "eshop-pro").Price);
        Assert.IsFalse(plans.Truncated);
        StringAssert.Contains(_fake.Calls.Single(c => c.Contains("products")), "/product_families/handle");
    }

    [TestMethod]
    public async Task RejectsAPlanTheFamilyDoesNotOfferWithoutWritingToMaxio()
    {
        var ex = await Assert.ThrowsExceptionAsync<SubscriptionException>(
            () => NewScope().Service.SubscribeAsync(User, Email, "not-a-plan"));

        Assert.AreEqual(SubscriptionFailure.InvalidPlan, ex.Failure);
        Assert.AreEqual(0, _fake.Count("POST /customers.json"));
        Assert.AreEqual(0, _fake.Count("POST /subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeConfirmsPlanPriceStateAndNextBillingDate()
    {
        var result = await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro");

        Assert.IsFalse(result.AlreadySubscribed);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual(299m, result.Subscription.Price);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(2030, result.Subscription.NextBillingDate!.Value.Year);

        var mine = await NewScope().Service.ListMySubscriptionsAsync(User);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.IsFalse(mine.Truncated);
    }

    [TestMethod]
    public async Task RepeatedSubscribeNeverCreatesASecondCustomerOrSubscription()
    {
        await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro");
        var again = await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro");

        Assert.IsTrue(again.AlreadySubscribed);
        Assert.AreEqual(1, _fake.Count("POST /customers.json"));
        Assert.AreEqual(1, _fake.Count("POST /subscriptions.json"));
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickReachesMaxioOnce()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try { return (await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro"), (SubscriptionFailure?)null); }
            catch (SubscriptionException ex) { return (null, ex.Failure); }
        }));

        Assert.AreEqual(1, _fake.Count("POST /customers.json"));
        Assert.AreEqual(1, _fake.Count("POST /subscriptions.json"));
        Assert.AreEqual(1, outcomes.Count(o => o.Item1 is { AlreadySubscribed: false }));
        Assert.IsTrue(outcomes.All(o => o.Item1 is not null || o.Item2 == SubscriptionFailure.InProgress));
    }

    [TestMethod]
    public async Task ConnectionDroppedAfterTheWriteIsSettledByReference()
    {
        _fake.DropConnectionAfterSubscriptionPost = true;

        var result = await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro");

        Assert.IsFalse(result.AlreadySubscribed);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual(1, _fake.Count("POST /subscriptions.json"), "the SDK must not resend a POST");
        Assert.AreEqual("Completed", await ClaimState("eshop-sub-"));
    }

    [TestMethod]
    public async Task UnsettledOutcomeIsRecordedAsUnknownAndSettlesOnRetry()
    {
        _fake.DropConnectionAfterSubscriptionPost = true;
        _fake.FailLookupsAfterDrop = true;

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionException>(
            () => NewScope().Service.SubscribeAsync(User, Email, "eshop-pro"));

        Assert.AreEqual(SubscriptionFailure.InProgress, ex.Failure);   // unknown, not failed
        Assert.AreEqual("Unknown", await ClaimState("eshop-sub-"));

        _fake.FailLookups = false;
        var retry = await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro");
        Assert.IsTrue(retry.AlreadySubscribed);
        Assert.AreEqual(1, _fake.Count("POST /subscriptions.json"));
        Assert.AreEqual("Completed", await ClaimState("eshop-sub-"));
    }

    [TestMethod]
    public async Task MaxioRefusalIsReportedAndTheClaimIsReleased()
    {
        _fake.RefuseSubscriptionPost = true;
        var ex = await Assert.ThrowsExceptionAsync<SubscriptionException>(
            () => NewScope().Service.SubscribeAsync(User, Email, "eshop-pro"));
        Assert.AreEqual(SubscriptionFailure.Rejected, ex.Failure);
        StringAssert.Contains(ex.Message, "No payment method");
        Assert.IsNull(await ClaimState("eshop-sub-"));

        _fake.RefuseSubscriptionPost = false;
        var ok = await NewScope().Service.SubscribeAsync(User, Email, "eshop-pro");
        Assert.IsFalse(ok.AlreadySubscribed);
    }

    [TestMethod]
    public async Task UsersWithoutSubscriptionsGetAnEmptyList()
    {
        var mine = await NewScope().Service.ListMySubscriptionsAsync("someone@else.test");
        Assert.AreEqual(0, mine.Subscriptions.Count);
    }

    [TestMethod]
    public void RefusesToStartWithoutMaxioApiKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "  ",
            ["Maxio:Subdomain"] = "placeholder",
            ["Maxio:ProductFamilyHandle"] = "placeholder-family"
        }).Build();

        var ex = Assert.ThrowsException<InvalidOperationException>(() => new ServiceCollection().AddMaxioSubscriptions(config));
        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }

    [TestMethod]
    public void BaseUrlOverrideIsUsedVerbatim()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "k",
            ["Maxio:Subdomain"] = "subdomain-that-must-not-be-used",
            ["Maxio:ProductFamilyHandle"] = "fam",
            ["Maxio:BaseUrl"] = "https://override.invalid"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddMaxioSubscriptions(config);
        services.AddHttpClient(MaxioServiceCollectionExtensions.HTTP_CLIENT_NAME).ConfigurePrimaryHttpMessageHandler(() => _fake);

        services.BuildServiceProvider().GetRequiredService<MaxioAdvancedBillingClient>()
            .Sites.ReadSite().GetAwaiter().GetResult();

        Assert.AreEqual(1, _fake.Count("GET /site.json"));
        Assert.AreEqual("override.invalid", _fake.Hosts.Single());
    }

    [TestMethod]
    public void SdkNeverResendsPostRequests()
    {
        var retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default();
        Assert.IsFalse(retry.HttpMethodsToRetry.Contains(HttpMethod.Post));
    }
}
