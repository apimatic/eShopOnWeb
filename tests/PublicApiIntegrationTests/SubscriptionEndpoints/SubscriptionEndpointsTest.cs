using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises routing/auth/wiring for the subscription endpoints with a substituted
/// <see cref="IMaxioSubscriptionService"/> - no real Maxio traffic. Maxio wire behavior itself is
/// covered by MaxioSubscriptionServiceTests in UnitTests against the real SDK's HTTP seam.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient CreateClient(FakeMaxioSubscriptionService fake)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioSubscriptionService>();
                services.AddSingleton<IMaxioSubscriptionService>(fake);
            });
        });
        return factory.CreateClient();
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task GetSubscriptionPlans_WithoutToken_ReturnsUnauthorized()
    {
        var client = CreateClient(new FakeMaxioSubscriptionService());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSubscriptionPlans_WithToken_ReturnsPlansFromService()
    {
        var fake = new FakeMaxioSubscriptionService();
        fake.Plans.Add(new SubscriptionPlanDto("eshop-pro", "Pro Plan", 29900, "month", 1));
        var client = CreateClient(fake);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(1, model!.Plans.Count);
        Assert.AreEqual("eshop-pro", model.Plans[0].Handle);
        Assert.AreEqual(299.00m, model.Plans[0].Price);
    }

    [TestMethod]
    public async Task CreateSubscription_WithValidPlan_UsesCallerIdentityFromToken()
    {
        var fake = new FakeMaxioSubscriptionService();
        var client = CreateClient(fake);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { PlanHandle = "eshop-pro" }));
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual("eshop-pro", model!.Subscription.PlanHandle);
        Assert.AreEqual("demouser@microsoft.com", fake.LastCustomer?.Reference);
    }

    [TestMethod]
    public async Task CreateSubscription_CalledTwice_ReturnsSameSubscriptionIdBothTimes()
    {
        var fake = new FakeMaxioSubscriptionService();
        var client = CreateClient(fake);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", JsonBody(new { PlanHandle = "eshop-pro" }));
        var second = await client.PostAsync("api/subscriptions", JsonBody(new { PlanHandle = "eshop-pro" }));

        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(firstModel!.Subscription.SubscriptionId, secondModel!.Subscription.SubscriptionId);
        Assert.AreEqual(1, fake.CreatedCount);
    }

    [TestMethod]
    public async Task CreateSubscription_MissingPlanHandle_ReturnsBadRequest()
    {
        var client = CreateClient(new FakeMaxioSubscriptionService());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { PlanHandle = "" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private class FakeMaxioSubscriptionService : IMaxioSubscriptionService
    {
        public List<SubscriptionPlanDto> Plans { get; } = new();
        public MaxioCustomerIdentity? LastCustomer { get; private set; }
        public int CreatedCount { get; private set; }
        private CustomerSubscriptionDto? _existing;

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(Plans);

        public Task<IReadOnlyList<CustomerSubscriptionDto>> GetSubscriptionsForCustomerAsync(MaxioCustomerIdentity customer, CancellationToken ct = default)
        {
            LastCustomer = customer;
            IReadOnlyList<CustomerSubscriptionDto> result = _existing is null
                ? Array.Empty<CustomerSubscriptionDto>()
                : new[] { _existing };
            return Task.FromResult(result);
        }

        public Task<CustomerSubscriptionDto> SubscribeAsync(MaxioCustomerIdentity customer, string planHandle, CancellationToken ct = default)
        {
            LastCustomer = customer;
            if (_existing is null)
            {
                CreatedCount++;
                _existing = new CustomerSubscriptionDto(1001, planHandle, "Pro Plan", 29900, "active", DateTimeOffset.UtcNow.AddMonths(1));
            }
            return Task.FromResult(_existing);
        }
    }
}
