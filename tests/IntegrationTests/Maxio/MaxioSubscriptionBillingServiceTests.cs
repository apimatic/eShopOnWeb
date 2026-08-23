using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public sealed class MaxioSubscriptionBillingServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public MaxioSubscriptionBillingServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDbContext<AppIdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppIdentityDbContext>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _catalogContext = _scope.ServiceProvider.GetRequiredService<CatalogContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    [Fact]
    public async Task DoubleSubscribeCreatesOneProviderSubscriptionAndReturnsCurrentState()
    {
        await AddUserAsync();
        var provider = new MaxioStubHandler();
        var service = CreateService(provider);

        var first = service.SubscribeAsync("shopper@example.com", "eshop-pro", CancellationToken.None);
        var second = service.SubscribeAsync("shopper@example.com", "eshop-pro", CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, provider.CustomerPostCount);
        Assert.Equal(1, provider.SubscriptionPostCount);
        Assert.All(results, result =>
        {
            Assert.Equal("eshop-pro", result.ProductHandle);
            Assert.Equal(29900, result.PriceInCents);
            Assert.Equal("active", result.State);
            Assert.NotNull(result.NextBillingAt);
        });

        var links = await _catalogContext.MaxioSubscriptionLinks.ToListAsync();
        Assert.Single(links);
        Assert.Equal(MaxioSubscriptionIntegrationStatus.Active, links[0].IntegrationStatus);

        var mine = await service.GetSubscriptionsAsync("shopper@example.com", CancellationToken.None);
        Assert.Single(mine);
        Assert.Equal("eshop-pro", mine[0].ProductHandle);

        var productRequests = provider.Requests.Where(request => request.Path.Contains("/products.json", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(productRequests);
        Assert.All(productRequests, request =>
        {
            Assert.Contains("handle%3Aeshop-subscribe", request.Path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("per_page=200", request.Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task TransportFailureDoesNotReplaySubscriptionPostAndLeavesReconciliationMarker()
    {
        await AddUserAsync();
        var provider = new MaxioStubHandler { FailSubscriptionPostWithTransportError = true };
        var service = CreateService(provider);

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            service.SubscribeAsync("shopper@example.com", "eshop-pro", CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("subscription_outcome_unresolved", exception.Code);
        Assert.Equal(1, provider.SubscriptionPostCount);

        var link = await _catalogContext.MaxioSubscriptionLinks.SingleAsync();
        Assert.Equal(MaxioSubscriptionIntegrationStatus.Ambiguous, link.IntegrationStatus);
    }

    private async Task AddUserAsync()
    {
        var result = await _userManager.CreateAsync(new ApplicationUser
        {
            UserName = "shopper@example.com",
            Email = "shopper@example.com",
            FirstName = "Shopper",
            LastName = "Example"
        });
        Assert.True(result.Succeeded);
    }

    private MaxioSubscriptionBillingService CreateService(MaxioStubHandler provider)
    {
        var callContext = new MaxioCallContext();
        var transport = new MaxioTransportHandler(callContext) { InnerHandler = provider };
        var httpClient = new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(8) };
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(2) }
        };
        clientOptions.Server.Production.Us.Site = "test-site";
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";

        return new MaxioSubscriptionBillingService(
            new MaxioAdvancedBillingClient(httpClient, clientOptions),
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe",
                BaseUrl = "https://maxio.test"
            }),
            _catalogContext,
            _userManager,
            new AsyncKeyedLock(),
            callContext,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    private sealed class MaxioStubHandler : HttpMessageHandler
    {
        private volatile bool _customerCreated;
        private volatile bool _subscriptionCreated;
        private string _subscriptionReference = "eshop-sub-test";
        private int _customerPostCount;
        private int _subscriptionPostCount;

        public bool FailSubscriptionPostWithTransportError { get; init; }
        public int CustomerPostCount => _customerPostCount;
        public int SubscriptionPostCount => _subscriptionPostCount;
        public ConcurrentBag<RequestRecord> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(new RequestRecord(request.Method, uri.AbsolutePath, uri.Query));

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """[{"product":{"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_price_point_handle":"default"}}]""");
            }

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return _customerCreated
                    ? Json(HttpStatusCode.OK, CustomerJson)
                    : Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/customers.json", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _customerPostCount);
                _customerCreated = true;
                return Json(HttpStatusCode.Created, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/subscriptions/lookup.json", StringComparison.Ordinal))
            {
                return _subscriptionCreated
                    ? Json(HttpStatusCode.OK, GetSubscriptionJson())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _subscriptionPostCount);
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using (var document = JsonDocument.Parse(body))
                {
                    _subscriptionReference = document.RootElement
                        .GetProperty("subscription")
                        .GetProperty("reference")
                        .GetString()!;
                }
                if (FailSubscriptionPostWithTransportError)
                {
                    throw new HttpRequestException("simulated connection reset");
                }

                await Task.Delay(25, cancellationToken);
                _subscriptionCreated = true;
                return Json(HttpStatusCode.Created, GetSubscriptionJson());
            }

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.Contains("/customers/123/subscriptions.json", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, $"[{GetSubscriptionJson()}]");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private const string CustomerJson =
            "{\"customer\":{\"id\":123,\"reference\":\"eshop-user-test\",\"first_name\":\"Shopper\",\"last_name\":\"Example\",\"email\":\"shopper@example.com\"}}";

        private string GetSubscriptionJson() =>
            "{\"subscription\":{\"id\":456,\"reference\":\"" + _subscriptionReference +
            "\",\"state\":\"active\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2026-09-24T00:00:00Z\",\"customer\":{\"id\":123,\"reference\":\"eshop-user-test\"},\"product\":{\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}}";
    }

    private sealed record RequestRecord(HttpMethod Method, string Path, string Query);
}
