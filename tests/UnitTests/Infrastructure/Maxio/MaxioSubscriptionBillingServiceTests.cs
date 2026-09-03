using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task ListPlansReadsConfiguredFamilyByHandleAndMapsPrice()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/product_families/handle%3Atest-family/products.json",
                request.RequestUri!.AbsoluteUri,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("include_archived=false", request.RequestUri.Query);
            Assert.Contains("per_page=200", request.RequestUri.Query);
            return Json(HttpStatusCode.OK, ProductListJson);
        });
        await using var context = CreateContext();
        var service = CreateService(handler, context);

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("basic-plan", plan.Handle);
        Assert.Equal(2900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task RepeatedSubscribeCreatesOnlyOneCustomerAndSubscription()
    {
        var subscriptionCreated = false;
        string? subscriptionReference = null;
        var customerPosts = 0;
        var subscriptionPosts = 0;

        var handler = new StubHandler((request, requestBody) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                return subscriptionCreated
                    ? Json(HttpStatusCode.OK, SubscriptionJson(subscriptionReference!))
                    : Json(HttpStatusCode.NotFound, string.Empty);
            }

            if (request.Method == HttpMethod.Get && path == "/products/handle/eshop-pro.json")
            {
                return Json(HttpStatusCode.OK, ProProductJson);
            }

            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.NotFound, string.Empty);
            }

            if (request.Method == HttpMethod.Get && path == "/site.json")
            {
                return Json(HttpStatusCode.OK,
                    """{"site":{"relationship_invoicing_enabled":false}}""");
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                customerPosts++;
                using var body = JsonDocument.Parse(requestBody!);
                var customer = body.RootElement.GetProperty("customer");
                Assert.Equal("shopper@example.com", customer.GetProperty("email").GetString());
                Assert.StartsWith("eshop-c-", customer.GetProperty("reference").GetString());
                return Json(HttpStatusCode.Created,
                    JsonSerializer.Serialize(new
                    {
                        customer = new
                        {
                            id = 77,
                            reference = customer.GetProperty("reference").GetString(),
                            email = "shopper@example.com"
                        }
                    }));
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                subscriptionPosts++;
                using var body = JsonDocument.Parse(requestBody!);
                var subscription = body.RootElement.GetProperty("subscription");
                Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
                Assert.Equal(77, subscription.GetProperty("customer_id").GetInt32());
                Assert.Equal("invoice", subscription.GetProperty("payment_collection_method").GetString());
                subscriptionReference = subscription.GetProperty("reference").GetString();
                subscriptionCreated = true;
                return Json(HttpStatusCode.Created, SubscriptionJson(subscriptionReference!));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        await using var context = CreateContext();
        var service = CreateService(handler, context);
        var user = new BillingUser("identity-user-1", "shopper@example.com");

        var subscriptions = await Task.WhenAll(
            service.SubscribeAsync(user, "eshop-pro", default),
            service.SubscribeAsync(user, "eshop-pro", default));
        var first = subscriptions[0];
        var second = subscriptions[1];

        Assert.Equal(9001, first.Id);
        Assert.Equal(first, second);
        Assert.Equal(1, customerPosts);
        Assert.Equal(1, subscriptionPosts);
        Assert.Equal(1, await context.SubscriptionProvisioningIntents.CountAsync());
        Assert.Equal(9001,
            (await context.SubscriptionProvisioningIntents.SingleAsync()).MaxioSubscriptionId);
    }

    private static MaxioSubscriptionBillingService CreateService(
        StubHandler handler,
        CatalogContext context)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = "unit-test-api-key",
                Password = "x"
            },
            Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(2) },
            Logging = new LoggingOptions
            {
                LoggerFactory = NullLoggerFactory.Instance,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "unit-test-api-key",
            Subdomain = "unit-test-site",
            ProductFamilyHandle = "test-family"
        });

        return new MaxioSubscriptionBillingService(
            client,
            settings,
            new SubscriptionProvisioningStore(context),
            new SubscriptionKeyedLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CatalogContext(options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string SubscriptionJson(string reference) => JsonSerializer.Serialize(new
    {
        subscription = new
        {
            id = 9001,
            state = "active",
            product_price_in_cents = 29900,
            current_period_ends_at = "2026-10-03T00:00:00Z",
            next_assessment_at = "2026-10-03T00:00:00Z",
            product_price_point_id = 501,
            reference,
            currency = "USD",
            customer = new { id = 77 },
            product = new
            {
                id = 102,
                name = "Pro Plan",
                handle = "eshop-pro",
                price_in_cents = 29900,
                product_price_point_name = "Default",
                product_price_point_handle = "default",
                product_family = new { handle = "test-family" }
            }
        }
    });

    private const string ProductListJson = """
        [{"product":{"id":101,"name":"Basic Plan","handle":"basic-plan","description":"Basic subscription","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"handle":"test-family"}}}]
        """;

    private const string ProProductJson = """
        {"product":{"id":102,"name":"Pro Plan","handle":"eshop-pro","description":"Pro subscription","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"handle":"test-family"}}}
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = _responder(request, LastBody);
            response.RequestMessage = request;
            return response;
        }
    }
}
