using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program>? _application;
    private FakeSubscriptionBillingService? _billing;
    private HttpClient? _client;

    [TestInitialize]
    public void Initialize()
    {
        _billing = new FakeSubscriptionBillingService();
        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISubscriptionBillingService>();
                    services.AddSingleton<ISubscriptionBillingService>(_billing);
                });
            });
        _client = _application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _application?.Dispose();
    }

    [TestMethod]
    [DataRow("GET", "/api/subscription-plans")]
    [DataRow("POST", "/api/subscriptions")]
    [DataRow("GET", "/api/my-subscriptions")]
    public async Task RejectsAnonymousCallers(string method, string route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new CreateSubscriptionRequest
            {
                ProductHandle = "selected-plan"
            });
        }

        using var response = await _client!.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, _billing!.CallCount);
    }

    [TestMethod]
    public async Task UsesJwtIdentityAndReturnsPlanAndSubscriptionContracts()
    {
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        using var plansResponse = await _client.GetAsync("/api/subscription-plans");
        using var createResponse = await _client.PostAsJsonAsync(
            "/api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "selected-plan" });
        using var mineResponse = await _client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, mineResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>();
        Assert.IsNotNull(created);
        Assert.AreEqual("selected-plan", created.PlanHandle);
        Assert.AreEqual(29900L, created.PriceInCents);
        Assert.AreEqual("active", created.State);
        Assert.IsNotNull(_billing!.LastUser);
        Assert.AreEqual("demouser@microsoft.com", _billing.LastUser.UserName);
        Assert.AreEqual("selected-plan", _billing.LastProductHandle);
        Assert.AreEqual(3, _billing.CallCount);
    }

    [TestMethod]
    public async Task MapsProviderFailureToSanitizedProblemDetails()
    {
        _billing!.Failure = new SubscriptionBillingException(
            SubscriptionBillingError.ProviderUnavailable,
            "Maxio is temporarily unavailable.",
            new InvalidOperationException("sensitive provider detail"));
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        using var response = await _client.GetAsync("/api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(body, "Maxio is temporarily unavailable.");
        Assert.IsFalse(body.Contains("sensitive provider detail", StringComparison.Ordinal));
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionDto Subscription = new(
            101,
            "selected-plan",
            "Selected plan",
            29900,
            1,
            "month",
            "active",
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"));

        public int CallCount { get; private set; }
        public ApplicationUser? LastUser { get; private set; }
        public string? LastProductHandle { get; private set; }
        public SubscriptionBillingException? Failure { get; set; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            ThrowIfConfigured();
            IReadOnlyList<SubscriptionPlanDto> result = new[]
            {
                new SubscriptionPlanDto(
                    "selected-plan",
                    "Selected plan",
                    "A test plan",
                    29900,
                    1,
                    "month",
                    false)
            };
            return Task.FromResult(result);
        }

        public Task<SubscriptionDto> SubscribeAsync(
            ApplicationUser user,
            string productHandle,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ThrowIfConfigured();
            LastUser = user;
            LastProductHandle = productHandle;
            return Task.FromResult(Subscription);
        }

        public Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ThrowIfConfigured();
            LastUser = user;
            IReadOnlyList<SubscriptionDto> result = new[] { Subscription };
            return Task.FromResult(result);
        }

        private void ThrowIfConfigured()
        {
            if (Failure != null)
            {
                throw Failure;
            }
        }
    }
}
