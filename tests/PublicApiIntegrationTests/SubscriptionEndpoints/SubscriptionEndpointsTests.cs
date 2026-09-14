using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    [TestMethod]
    public async Task ListPlansRequiresAuthentication()
    {
        var client = CreateClient(out _);

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsPlansForAuthenticatedUser()
    {
        var stub = new StubSubscriptionService
        {
            Plans = new List<SubscriptionPlan>
            {
                new SubscriptionPlan { Handle = "eshop-pro", Name = "Pro Plan", PriceAmount = 299m, Interval = 1, IntervalUnit = "month" },
                new SubscriptionPlan { Handle = "basic-plan", Name = "Basic Plan", PriceAmount = 29m, Interval = 1, IntervalUnit = "month" }
            }
        };
        var client = CreateClient(out _, stub);
        client.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.Plans.Count);
        Assert.AreEqual("eshop-pro", model.Plans[0].Handle);
        Assert.AreEqual(299m, model.Plans[0].PriceAmount);
    }

    [TestMethod]
    public async Task SubscribeRequiresAuthentication()
    {
        var client = CreateClient(out _);

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "eshop-pro" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionForTheTokenCaller()
    {
        var stub = new StubSubscriptionService
        {
            Enrollment = new SubscriptionEnrollment
            {
                IsNew = true,
                Subscription = new SubscriptionInfo
                {
                    SubscriptionId = 123,
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    PriceAmount = 299m,
                    State = "active",
                    NextBillingDate = DateTimeOffset.Parse("2026-10-08T00:00:00Z")
                }
            }
        };
        var client = CreateClient(out _, stub);
        client.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "eshop-pro" }));

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.IsNew);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        // The caller's identity must come from the token, not the request body.
        Assert.IsNotNull(stub.LastCustomer);
        Assert.AreEqual("demouser@microsoft.com", stub.LastCustomer!.Email);
        Assert.AreEqual("eshop-pro", stub.LastPlanHandle);
    }

    [TestMethod]
    public async Task SubscribeSurfacesMaxioRejectionWithItsStatus()
    {
        var stub = new StubSubscriptionService
        {
            ExceptionToThrow = new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "Product with API Handle 'no-such-plan' does not exist for this site.")
        };
        var client = CreateClient(out _, stub);
        client.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "does not exist");
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsCallersSubscriptions()
    {
        var stub = new StubSubscriptionService
        {
            Subscriptions = new List<SubscriptionInfo>
            {
                new SubscriptionInfo
                {
                    SubscriptionId = 456,
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    PriceAmount = 299m,
                    State = "active"
                }
            }
        };
        var client = CreateClient(out _, stub);
        client.DefaultRequestHeaders.Authorization = Bearer(ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].PlanHandle);
        Assert.IsNotNull(stub.LastCustomer);
        Assert.AreEqual("admin@microsoft.com", stub.LastCustomer!.Email);
    }

    private static HttpClient CreateClient(out StubSubscriptionService stub, StubSubscriptionService? service = null)
    {
        var instance = service ?? new StubSubscriptionService();
        stub = instance;
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMaxioSubscriptionService>();
                    services.AddSingleton<IMaxioSubscriptionService>(instance);
                });
            });
        return factory.CreateClient();
    }

    private static AuthenticationHeaderValue Bearer(string token) => new AuthenticationHeaderValue("Bearer", token);

    private static StringContent JsonBody(object body)
    {
        var json = JsonSerializer.Serialize(body);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class StubSubscriptionService : IMaxioSubscriptionService
    {
        public IReadOnlyList<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();
        public SubscriptionEnrollment? Enrollment { get; set; }
        public IReadOnlyList<SubscriptionInfo> Subscriptions { get; set; } = Array.Empty<SubscriptionInfo>();
        public Exception? ExceptionToThrow { get; set; }
        public MaxioCustomer? LastCustomer { get; private set; }
        public string? LastPlanHandle { get; private set; }
        public int ListPlansCalls { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        {
            ListPlansCalls++;
            return Task.FromResult(Plans);
        }

        public Task<SubscriptionEnrollment> SubscribeAsync(MaxioCustomer customer, string planHandle, CancellationToken cancellationToken)
        {
            LastCustomer = customer;
            LastPlanHandle = planHandle;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
            return Task.FromResult(Enrollment ?? new SubscriptionEnrollment());
        }

        public Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken)
        {
            LastCustomer = customer;
            return Task.FromResult(Subscriptions);
        }
    }
}
