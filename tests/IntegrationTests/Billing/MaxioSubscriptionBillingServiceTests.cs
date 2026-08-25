#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string CustomerReference = "eshop-user-user-1";

    [Fact]
    public async Task ConcurrentSubscribeCreatesOneUpstreamSubscriptionAndReturnsSameResult()
    {
        string? subscriptionReference = null;
        var handler = new SequencedHandler(new Func<CapturedRequest, HttpResponseMessage>[]
        {
            _ => Empty(HttpStatusCode.NotFound),
            _ => Json(HttpStatusCode.Created, CustomerJson),
            _ => Json(HttpStatusCode.OK, ProductJson),
            _ => Empty(HttpStatusCode.NotFound),
            request =>
            {
                using var body = JsonDocument.Parse(request.Body!);
                subscriptionReference = body.RootElement
                    .GetProperty("subscription")
                    .GetProperty("reference")
                    .GetString();
                return Json(HttpStatusCode.Created, SubscriptionJson(subscriptionReference!));
            },
            _ => Json(HttpStatusCode.OK, CustomerJson),
            _ => Json(HttpStatusCode.OK, ProductJson),
            _ => Json(HttpStatusCode.OK, SubscriptionJson(subscriptionReference!))
        });
        var client = CreateClient(handler);
        var databaseRoot = new InMemoryDatabaseRoot();
        var keyLock = new SubscriptionKeyLock();
        var requestContext = new MaxioRequestContext();
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "not-used-by-stub",
            Subdomain = "not-used-by-stub",
            ProductFamilyHandle = "billing-family",
            BaseUrl = "https://maxio.test"
        });
        await using var firstDb = CreateIdentityContext(databaseRoot);
        await using var secondDb = CreateIdentityContext(databaseRoot);
        var firstService = CreateService(client, options, firstDb, keyLock, requestContext);
        var secondService = CreateService(client, options, secondDb, keyLock, requestContext);
        var customer = new BillingCustomer("user-1", "demo@example.com", "Demo", "Customer");

        var results = await Task.WhenAll(
            firstService.SubscribeAsync(customer, "pro-test", default),
            secondService.SubscribeAsync(customer, "pro-test", default));

        Assert.Equal(42, results[0].Id);
        Assert.Equal(results[0], results[1]);
        Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Post));
        var subscriptionPosts = handler.Requests
            .Where(request => request.Method == HttpMethod.Post &&
                              request.Body?.Contains("\"product_handle\"", StringComparison.Ordinal) == true)
            .ToArray();
        var subscriptionPost = Assert.Single(subscriptionPosts);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionPost.Body);
        Assert.Single(firstDb.SubscriptionEnrollments);
        Assert.Equal("Confirmed", firstDb.SubscriptionEnrollments.Single().Status);
    }

    private static MaxioSubscriptionBillingService CreateService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        AppIdentityDbContext dbContext,
        SubscriptionKeyLock keyLock,
        MaxioRequestContext requestContext) =>
        new(
            client,
            options,
            dbContext,
            keyLock,
            requestContext,
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    private static AppIdentityDbContext CreateIdentityContext(InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase("MaxioSubscriptionTests", databaseRoot)
            .Options;
        return new AppIdentityDbContext(options);
    }

    private static MaxioAdvancedBillingClient CreateClient(HttpMessageHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(2)
            }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Empty(HttpStatusCode statusCode) => new(statusCode);

    private const string CustomerJson =
        """
        {"customer":{"id":123,"first_name":"Demo","last_name":"Customer","email":"demo@example.com","reference":"eshop-user-user-1"}}
        """;

    private const string ProductJson =
        """
        {"product":{"id":77,"name":"Pro Plan","handle":"pro-test","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":5,"name":"Billing plans","handle":"billing-family"}}}
        """;

    private static string SubscriptionJson(string reference) => JsonSerializer.Serialize(new
    {
        subscription = new
        {
            id = 42,
            reference,
            state = "active",
            product_price_in_cents = 29900,
            next_assessment_at = "2030-01-01T00:00:00Z",
            currency = "USD",
            product = new
            {
                id = 77,
                name = "Pro Plan",
                handle = "pro-test",
                price_in_cents = 29900,
                interval = 1,
                interval_unit = "month",
                product_family = new { id = 5, name = "Billing plans", handle = "billing-family" }
            },
            customer = new
            {
                id = 123,
                first_name = "Demo",
                last_name = "Customer",
                email = "demo@example.com",
                reference = CustomerReference
            }
        }
    });

    private sealed record CapturedRequest(HttpMethod Method, string PathAndQuery, string? Body);

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<CapturedRequest, HttpResponseMessage>> _responses;

        public SequencedHandler(IEnumerable<Func<CapturedRequest, HttpResponseMessage>> responses) =>
            _responses = new ConcurrentQueue<Func<CapturedRequest, HttpResponseMessage>>(responses);

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Enqueue(captured);
            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException("The SDK sent more requests than expected.");
            }

            return response(captured);
        }
    }
}
