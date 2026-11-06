using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private sealed class FakeMaxio : HttpMessageHandler
    {
        public int CreateSubscriptionPosts;
        public int CreateCustomerPosts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/products.json"))
                return Json(HttpStatusCode.OK, """[{"product":{"id":1,"handle":"plan-a","name":"Plan A","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""");
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
                return Json(HttpStatusCode.NotFound, "");
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                Interlocked.Increment(ref CreateCustomerPosts);
                return Json(HttpStatusCode.Created, """{"customer":{"id":7,"reference":"r"}}""");
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
                return Json(HttpStatusCode.NotFound, "");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                Interlocked.Increment(ref CreateSubscriptionPosts);
                Thread.Sleep(200);
                return Json(HttpStatusCode.Created, """{"subscription":{"id":99,"state":"active","product_price_in_cents":2900,"next_assessment_at":"2030-01-01T00:00:00Z","product":{"handle":"plan-a","name":"Plan A"}}}""");
            }
            return Json(HttpStatusCode.NotFound, "");
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static HttpClient ClientWith(FakeMaxio fake, bool authenticated = true)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<MaxioAdvancedBillingClient>();
            services.AddSingleton(new MaxioAdvancedBillingClient(new HttpClient(fake), new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
                Server = { Production = { Us = { BaseUrl = "http://maxio.test" } } }
            }));
        }));
        var client = factory.CreateClient();
        if (authenticated)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var client = ClientWith(new FakeMaxio(), authenticated: false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "plan-a" })).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        using var client = ClientWith(new FakeMaxio());
        var body = await client.GetStringAsync("api/subscription-plans");
        StringAssert.Contains(body, "plan-a");
    }

    [TestMethod]
    public async Task RejectsPlanNotOffered()
    {
        using var client = ClientWith(new FakeMaxio());
        var res = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "not-offered" });
        Assert.AreEqual((HttpStatusCode)422, res.StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentSubscribeReachesMaxioOnce()
    {
        var fake = new FakeMaxio();
        using var client = ClientWith(fake);
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync("api/subscriptions", new { planHandle = "plan-a" })));

        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(1, fake.CreateCustomerPosts);
        Assert.AreEqual(1, fake.CreateSubscriptionPosts);
    }
}
