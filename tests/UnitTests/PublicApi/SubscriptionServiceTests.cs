using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class SubscriptionServiceTests
{
    private const string PlansJson = """[{"product":{"id":1,"handle":"plan-a","name":"A","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""";
    private const string CustomerJson = """{"customer":{"id":7,"reference":"U@X.COM"}}""";
    private const string SubscriptionJson = """{"subscription":{"id":99,"state":"active","reference":"eshop:U@X.COM:plan-a","product":{"handle":"plan-a","name":"A","price_in_cents":2900,"interval":1,"interval_unit":"month"}}}""";

    private sealed class Stub : HttpMessageHandler
    {
        public int SubscriptionPosts;
        public bool CreateSubscriptionDropsConnection;
        public bool SubscriptionExists;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/product_families/")) return Json(PlansJson);
            if (path.StartsWith("/customers/lookup")) return Json(CustomerJson);
            if (path == "/subscriptions/lookup.json")
                return SubscriptionExists ? Json(SubscriptionJson) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref SubscriptionPosts);
                if (CreateSubscriptionDropsConnection)
                {
                    SubscriptionExists = true; // Maxio acted, but the response is lost
                    throw new HttpRequestException("connection reset");
                }
                SubscriptionExists = true;
                return Json(SubscriptionJson);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static (Func<SubscriptionService> Factory, Stub Handler, DbContextOptions<CatalogContext> Db) Arrange()
    {
        var handler = new Stub();
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" },
            Retry = RetryOptions.Disabled()
        });
        var db = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" };
        return (() => new SubscriptionService(client, settings, new CatalogContext(db), users, NullLogger<SubscriptionService>.Instance), handler, db);
    }

    [Fact]
    public async Task ConcurrentSubscribe_CreatesOneSubscription()
    {
        var (factory, handler, _) = Arrange();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return (await factory().SubscribeAsync("u@x.com", "plan-a", default)).Created; }
            catch (SubscriptionException ex) { Assert.Equal(409, ex.StatusCode); return false; }
        }));

        Assert.Equal(1, handler.SubscriptionPosts);
        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task ConnectionFailureOnCreate_IsSettledByRereadingMaxio()
    {
        var (factory, handler, _) = Arrange();
        handler.CreateSubscriptionDropsConnection = true;

        var (subscription, created) = await factory().SubscribeAsync("u@x.com", "plan-a", default);

        Assert.Equal(99, subscription.Id);
        Assert.True(created);
        Assert.Equal(1, handler.SubscriptionPosts);
    }

    [Fact]
    public async Task UnofferedPlan_IsRejectedBeforeAnyWrite()
    {
        var (factory, handler, _) = Arrange();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => factory().SubscribeAsync("u@x.com", "not-offered", default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, handler.SubscriptionPosts);
    }
}
