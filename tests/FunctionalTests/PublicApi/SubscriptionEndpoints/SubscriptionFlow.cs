using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.eShopWeb.FunctionalTests.Web.Api;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

public class SubscriptionFlow
{
    [Fact]
    public async Task EndpointsRequireJwtAuthentication()
    {
        await using var factory = new SubscriptionApiApplication();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" })).StatusCode);
    }

    [Fact]
    public async Task SubscribeIsIdempotentAndAppearsInAccount()
    {
        await using var factory = new SubscriptionApiApplication();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetFromJsonAsync<SubscriptionPlansResponse>("/api/subscription-plans");
        Assert.Contains(plansResponse!.Plans, x => x.Handle == "eshop-pro" && x.PriceInCents == 29900);

        var requests = Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" }));
        var responses = await Task.WhenAll(requests);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var subscriptions = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<SubscriptionDto>()));
        Assert.All(subscriptions, subscription => Assert.Equal(7001, subscription!.Id));
        Assert.Equal(1, factory.Maxio.SubscriptionCreateCount);
        Assert.Equal(1, factory.Maxio.CustomerCreateCount);

        var mine = await client.GetFromJsonAsync<MySubscriptionsResponse>("/api/my-subscriptions");
        var subscription = Assert.Single(mine!.Subscriptions);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.NotNull(subscription.NextBillingAt);
    }

    private sealed class SubscriptionApiApplication : WebApplicationFactory<Program>
    {
        private readonly string _catalogDatabaseName = $"SubscriptionCatalog-{Guid.NewGuid()}";
        private readonly string _identityDatabaseName = $"SubscriptionIdentity-{Guid.NewGuid()}";

        public FakeMaxioBillingClient Maxio { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
                services.AddDbContext<CatalogContext>(options =>
                    options.UseInMemoryDatabase(_catalogDatabaseName));
                services.AddDbContext<AppIdentityDbContext>(options =>
                    options.UseInMemoryDatabase(_identityDatabaseName));
                services.RemoveAll<IMaxioBillingClient>();
                services.AddSingleton<IMaxioBillingClient>(Maxio);
            });
        }
    }

    private sealed class FakeMaxioBillingClient : IMaxioBillingClient
    {
        private readonly ConcurrentDictionary<string, MaxioCustomer> _customers = new();
        private readonly ConcurrentDictionary<string, SubscriptionDto> _subscriptions = new();
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(new[]
            {
                new SubscriptionPlanDto("basic-plan", "Basic Plan", "Basic", 2900, 1, "month", "Default"),
                new SubscriptionPlanDto("eshop-pro", "Pro Plan", "Pro", 29900, 1, "month", "Default")
            });

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            _customers.TryGetValue(reference, out var customer);
            return Task.FromResult(customer);
        }

        public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, CancellationToken cancellationToken)
        {
            var created = _customers.GetOrAdd(customer.Reference, reference =>
            {
                Interlocked.Increment(ref _customerCreateCount);
                return new MaxioCustomer(5001, reference);
            });
            return Task.FromResult(created);
        }

        public Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public Task<SubscriptionDto> CreateSubscriptionAsync(
            string productHandle,
            string customerReference,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            var created = _subscriptions.GetOrAdd(subscriptionReference, _ =>
            {
                Interlocked.Increment(ref _subscriptionCreateCount);
                return new SubscriptionDto(
                    7001,
                    productHandle,
                    "Pro Plan",
                    29900,
                    1,
                    "month",
                    "Default",
                    "active",
                    DateTimeOffset.UtcNow.AddMonths(1));
            });
            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(
            long customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>(_subscriptions.Values.ToList());
    }
}
