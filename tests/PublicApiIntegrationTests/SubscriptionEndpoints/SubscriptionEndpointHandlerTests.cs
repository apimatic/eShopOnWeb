using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.UnitTests.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Runs the three endpoint handlers against the real service registration (AddMaxioBilling, EF claims, SDK client) with the
/// Maxio transport replaced by FakeMaxio, executing the returned IResult into a MemoryStream so 2xx JSON bodies can be asserted.
/// </summary>
[TestClass]
public class SubscriptionEndpointHandlerTests
{
    private sealed class Host
    {
        public FakeMaxio Maxio { get; } = new();
        public ServiceProvider Provider { get; }

        public Host()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-key",
                ["Maxio:Subdomain"] = "test-site",
                ["Maxio:ProductFamilyHandle"] = "test-family"
            }).Build();
            var dbName = Guid.NewGuid().ToString();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddMaxioBilling(config);
            services.RemoveAll<MaxioAdvancedBillingClient>();
            services.AddSingleton(new MaxioAdvancedBillingClient(new HttpClient(Maxio), new MaxioAdvancedBillingClientOptions
            {
                Retry = RetryOptions.Disabled(),
                Server = new ServerOptions { Production = { Us = { BaseUrl = "https://maxio.test" } } }
            }));
            Provider = services.BuildServiceProvider();
        }

        public async Task<(int Status, JsonDocument Body, HttpResponse Response)> Call(string userName, Func<ISubscriptionService, HttpContext, Task<IResult>> handler)
        {
            var scope = Provider.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userName) }, "test"));
            http.Response.Body = new MemoryStream();

            var result = await handler(scope.ServiceProvider.GetRequiredService<ISubscriptionService>(), http);
            await result.ExecuteAsync(http);

            http.Response.Body.Position = 0;
            var text = await new StreamReader(http.Response.Body).ReadToEndAsync();
            return (http.Response.StatusCode, JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text), http.Response);
        }

        public Task<(int Status, JsonDocument Body, HttpResponse Response)> Plans(string user) =>
            Call(user, (svc, http) => new ListSubscriptionPlansEndpoint().HandleAsync(new ListSubscriptionPlansRequest(), svc, http.RequestAborted));

        public Task<(int Status, JsonDocument Body, HttpResponse Response)> Subscribe(string user, string plan) =>
            Call(user, (svc, http) => new SubscribeEndpoint().HandleAsync(new SubscribeRequest { PlanHandle = plan }, svc, http));

        public Task<(int Status, JsonDocument Body, HttpResponse Response)> Mine(string user) =>
            Call(user, (svc, http) => new MySubscriptionsEndpoint().HandleAsync(new MySubscriptionsRequest(), svc, http));
    }

    private static string NewUser() => $"it-{Guid.NewGuid():N}@example.com";

    [TestMethod]
    public async Task Plans_AreReadFromTheProvider_NothingIsHardcoded()
    {
        var host = new Host();
        host.Maxio.Plans.Clear();
        host.Maxio.Plans.Add(new("zz-invented-plan", "Invented", 1234));

        var (status, body, _) = await host.Plans(NewUser());

        Assert.AreEqual(200, status);
        var plans = body.RootElement.GetProperty("plans");
        Assert.AreEqual(1, plans.GetArrayLength());
        Assert.AreEqual("zz-invented-plan", plans[0].GetProperty("handle").GetString());
        Assert.AreEqual(1234, plans[0].GetProperty("priceInCents").GetInt64());
        Assert.AreEqual("month", plans[0].GetProperty("intervalUnit").GetString());
        Assert.IsFalse(body.RootElement.GetProperty("isPartial").GetBoolean());
    }

    [TestMethod]
    public async Task Plans_PartialListIsFlaggedInTheResponse()
    {
        var host = new Host();
        host.Maxio.Plans.Clear();
        for (var i = 0; i < 1000; i++) host.Maxio.Plans.Add(new($"p{i}", $"P{i}", 1));

        var (status, body, _) = await host.Plans(NewUser());

        Assert.AreEqual(200, status);
        Assert.IsTrue(body.RootElement.GetProperty("isPartial").GetBoolean());
    }

    [TestMethod]
    public async Task Subscribe_Twice_Creates201ThenReplays200_WithASingleProviderSubscription()
    {
        var host = new Host();
        var user = NewUser();

        var (s1, first, r1) = await host.Subscribe(user, "fake-pro");
        var (s2, second, _) = await host.Subscribe(user, "fake-pro");

        Assert.AreEqual(201, s1);
        Assert.AreEqual("api/my-subscriptions", r1.Headers.Location.ToString());
        Assert.AreEqual(200, s2);
        Assert.AreEqual(first.RootElement.GetProperty("subscriptionId").GetInt32(), second.RootElement.GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual("fake-pro", first.RootElement.GetProperty("planHandle").GetString());
        Assert.AreEqual("Fake Pro", first.RootElement.GetProperty("planName").GetString());
        Assert.AreEqual(9900, first.RootElement.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual("active", first.RootElement.GetProperty("state").GetString());
        Assert.IsTrue(first.RootElement.TryGetProperty("nextBillingAt", out var next) && next.ValueKind == JsonValueKind.String);
        Assert.AreEqual(1, host.Maxio.Subscriptions.Count);
        Assert.AreEqual(1, host.Maxio.Customers.Count);
    }

    [TestMethod]
    public async Task MySubscriptions_ShowsOnlyTheCallersSubscriptions_IdentityComesFromTheToken()
    {
        var host = new Host();
        var userA = NewUser();
        var userB = NewUser();
        await host.Subscribe(userA, "fake-basic");

        var (_, mineA, _) = await host.Mine(userA);
        var (_, mineB, _) = await host.Mine(userB);

        Assert.AreEqual(1, mineA.RootElement.GetProperty("subscriptions").GetArrayLength());
        Assert.AreEqual("fake-basic", mineA.RootElement.GetProperty("subscriptions")[0].GetProperty("planHandle").GetString());
        Assert.AreEqual(0, mineB.RootElement.GetProperty("subscriptions").GetArrayLength());
    }

    [TestMethod]
    public async Task Subscribe_ConnectionDropsAfterTheWrite_IsReconciledToASuccess()
    {
        var host = new Host();
        var user = NewUser();
        host.Maxio.Intercept = (req, _) =>
        {
            if (req.Method != HttpMethod.Post || !req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")) return null;
            host.Maxio.Subscriptions.Add(new FakeMaxio.FakeSubscription(9001, host.Maxio.Customers.Values.First(), "fake-pro", $"eshop-sub:{user}:fake-pro"));
            throw new HttpRequestException("connection reset");
        };

        var (status, body, _) = await host.Subscribe(user, "fake-pro");

        Assert.AreEqual(201, status);
        Assert.AreEqual(9001, body.RootElement.GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_ConnectionDropsAndNothingIsFound_Returns202Unknown()
    {
        var host = new Host();
        host.Maxio.Intercept = (req, _) =>
            req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json") ? throw new HttpRequestException("reset") : null;

        var (status, body, _) = await host.Subscribe(NewUser(), "fake-pro");

        Assert.AreEqual(202, status);
        Assert.AreEqual("Unknown", body.RootElement.GetProperty("state").GetString());
    }

    [TestMethod]
    public async Task OptionsValidation_RefusesToStartWithoutTheApiKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family"
        }).Build();
        using var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMaxioBilling(config))
            .Build();

        // ValidateOnStart: the host must refuse to start, not fail on the first request.
        var ex = await Assert.ThrowsExceptionAsync<Microsoft.Extensions.Options.OptionsValidationException>(() => host.StartAsync());

        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }
}
