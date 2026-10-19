using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static StringContent SubscribeBody(string planHandle) =>
        new($"{{\"planHandle\":\"{planHandle}\"}}", Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        using var factory = new SubscriptionApiFactory(MaxioStub.NewUserHappyPath());
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"))).StatusCode);
    }

    [TestMethod]
    public async Task ListsNonArchivedPlansOfTheConfiguredFamily()
    {
        var stub = MaxioStub.NewUserHappyPath();
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;

        CollectionAssert.AreEquivalent(new[] { "test-pro", "test-basic" }, model.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(299m, model.Plans.Single(p => p.Handle == "test-pro").Price);
        Assert.IsFalse(model.Truncated);
        var query = stub.Requests.Single(r => r.Route == MaxioStub.ProductsRoute).Query;
        StringAssert.Contains(query, "per_page=200");
    }

    [TestMethod]
    public async Task SubscribesANewUserAndReflectsItInTheirAccount()
    {
        var stub = MaxioStub.NewUserHappyPath();
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsTrue(model.Created);
        Assert.AreEqual("test-pro", model.Subscription.PlanHandle);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), model.Subscription.NextBillingDate);

        var customerBody = stub.Bodies(MaxioStub.CreateCustomerRoute).Single()!;
        StringAssert.Contains(customerBody, "\"reference\":\"eshop:demouser@microsoft.com\"");
        StringAssert.Contains(customerBody, "\"email\":\"demouser@microsoft.com\"");
        var subscriptionBody = stub.Bodies(MaxioStub.CreateSubscriptionRoute).Single()!;
        StringAssert.Contains(subscriptionBody, "\"product_handle\":\"test-pro\"");
        StringAssert.Contains(subscriptionBody, $"\"customer_id\":{MaxioStub.CustomerId}");
        StringAssert.Contains(subscriptionBody, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(subscriptionBody, "\"reference\":\"eshop-sub:");

        stub.On(MaxioStub.CustomerSubscriptionsRoute, HttpStatusCode.OK, $"[{MaxioStub.Subscription()}]");
        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(MaxioStub.SubscriptionId, mine.Subscriptions.Single().Id);
    }

    [TestMethod]
    public async Task DoubleClickCreatesExactlyOneCustomerAndOneSubscription()
    {
        var stub = MaxioStub.NewUserHappyPath();
        stub.On(MaxioStub.CreateSubscriptionRoute, async _ =>
        {
            await Task.Delay(500);   // hold the first request inside the provider call
            return MaxioStub.Json(HttpStatusCode.Created, MaxioStub.Subscription());
        });
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync("api/subscriptions", SubscribeBody("test-pro"))));

        Assert.AreEqual(1, stub.Count(MaxioStub.CreateCustomerRoute));
        Assert.AreEqual(1, stub.Count(MaxioStub.CreateSubscriptionRoute));
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict or HttpStatusCode.OK),
            string.Join(",", responses.Select(r => (int)r.StatusCode)));

        // A later repeat is answered with the existing subscription, not a new one.
        stub.On(MaxioStub.CustomerSubscriptionsRoute, HttpStatusCode.OK, $"[{MaxioStub.Subscription()}]");
        var repeat = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        Assert.AreEqual(1, stub.Count(MaxioStub.CreateSubscriptionRoute));
    }

    [TestMethod]
    public async Task RejectsAPlanOutsideTheConfiguredFamilyWithoutCallingMaxioWrites()
    {
        var stub = MaxioStub.NewUserHappyPath();
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("api/subscriptions", SubscribeBody("not-a-plan"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("api/subscriptions", SubscribeBody("old-plan"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.AreEqual(0, stub.Count(MaxioStub.CreateCustomerRoute));
        Assert.AreEqual(0, stub.Count(MaxioStub.CreateSubscriptionRoute));
    }

    [TestMethod]
    public async Task ConnectionDropOnCreateSubscriptionIsSettledByReference()
    {
        var stub = MaxioStub.NewUserHappyPath();
        // The write reaches Maxio but the connection drops before the answer arrives...
        stub.On(MaxioStub.CreateSubscriptionRoute, _ => throw new HttpRequestException("connection reset"));
        // ...and the lookup by the reference we sent finds it.
        stub.On(MaxioStub.SubscriptionLookupRoute, HttpStatusCode.OK, MaxioStub.Subscription());
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, stub.Count(MaxioStub.CreateSubscriptionRoute), "a POST must never be resent");
        var sentReference = ExtractReference(stub.Bodies(MaxioStub.CreateSubscriptionRoute).Single()!);
        var lookupQuery = stub.Requests.Single(r => r.Route == MaxioStub.SubscriptionLookupRoute).Query!;
        StringAssert.Contains(Uri.UnescapeDataString(lookupQuery), sentReference);
    }

    [TestMethod]
    public async Task ConnectionDropOnCreateSubscriptionThatDidNotLandReleasesTheClaim()
    {
        var stub = MaxioStub.NewUserHappyPath();
        stub.On(MaxioStub.CreateSubscriptionRoute, _ => throw new HttpRequestException("connection reset"));
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.AreEqual(1, stub.Count(MaxioStub.SubscriptionLookupRoute));

        // Not found at Maxio → the claim was released, so a retry goes through.
        stub.On(MaxioStub.CreateSubscriptionRoute, HttpStatusCode.Created, MaxioStub.Subscription());
        var retry = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));
        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode);
    }

    [TestMethod]
    public async Task ConnectionDropOnCreateCustomerIsSettledByReference()
    {
        var stub = MaxioStub.NewUserHappyPath();
        var lookups = 0;
        stub.On(MaxioStub.CustomerLookupRoute, _ => Task.FromResult(++lookups == 1
            ? MaxioStub.Json(HttpStatusCode.NotFound, "")
            : MaxioStub.Json(HttpStatusCode.OK, MaxioStub.Customer())));
        stub.On(MaxioStub.CreateCustomerRoute, _ => throw new HttpRequestException("connection reset"));
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, stub.Count(MaxioStub.CreateCustomerRoute), "a POST must never be resent");
        Assert.AreEqual(2, stub.Count(MaxioStub.CustomerLookupRoute));
        StringAssert.Contains(stub.Bodies(MaxioStub.CreateSubscriptionRoute).Single(), $"\"customer_id\":{MaxioStub.CustomerId}");
    }

    [TestMethod]
    public async Task MaxioValidationErrorIsReturnedAs422AndTheClaimIsReleased()
    {
        var stub = MaxioStub.NewUserHappyPath();
        stub.On(MaxioStub.CreateSubscriptionRoute, HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Product is not available\"]}");
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Product is not available");

        stub.On(MaxioStub.CreateSubscriptionRoute, HttpStatusCode.Created, MaxioStub.Subscription());
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"))).StatusCode);
    }

    [TestMethod]
    public async Task AfterARestartAnExistingMaxioSubscriptionIsAdoptedNotDuplicated()
    {
        // Fresh process (empty claim store), but Maxio already has this user's customer and subscription.
        var stub = MaxioStub.NewUserHappyPath();
        stub.On(MaxioStub.CustomerLookupRoute, HttpStatusCode.OK, MaxioStub.Customer());
        stub.On(MaxioStub.CustomerSubscriptionsRoute, HttpStatusCode.OK, $"[{MaxioStub.Subscription()}]");
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(1, mine.Subscriptions.Count);

        var response = await client.PostAsync("api/subscriptions", SubscribeBody("test-pro"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, stub.Count(MaxioStub.CreateCustomerRoute));
        Assert.AreEqual(0, stub.Count(MaxioStub.CreateSubscriptionRoute));

        // A different plan is refused rather than creating a second subscription.
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsync("api/subscriptions", SubscribeBody("test-basic"))).StatusCode);
    }

    [TestMethod]
    public async Task MaxioOutageIsReportedAsBadGateway()
    {
        var stub = MaxioStub.NewUserHappyPath();
        stub.On(MaxioStub.ProductsRoute, _ => throw new HttpRequestException("no route to host"));
        using var factory = new SubscriptionApiFactory(stub);
        var client = factory.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("maxio.test.invalid"), "no provider URL on the wire");
    }

    [TestMethod]
    public void HostRefusesToStartWithoutMaxioApiKey()
    {
        using var factory = new SubscriptionApiFactory(MaxioStub.NewUserHappyPath(), new Dictionary<string, string?> { ["Maxio:ApiKey"] = " " });

        var ex = Assert.ThrowsException<OptionsValidationException>(() => factory.CreateClient());
        StringAssert.Contains(ex.Message, "ApiKey");
    }

    private static string ExtractReference(string subscriptionBody)
    {
        const string marker = "\"reference\":\"";
        var start = subscriptionBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return subscriptionBody[start..subscriptionBody.IndexOf('"', start)];
    }
}
