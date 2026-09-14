#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task PendingDuplicateReturnsInProgressWithoutAnotherProviderLookupOrWrite()
    {
        var handler = new SubscriptionFlowHandler();
        using var httpClient = new HttpClient(handler);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new CatalogContext(dbOptions);
        db.SubscriptionEnrollments.Add(new(
            "immutable-user-id",
            "test-plan",
            "eshop-s-existing-pending-claim"));
        await db.SaveChangesAsync();
        var service = new MaxioSubscriptionBillingService(
            new MaxioAdvancedBillingClient(httpClient, clientOptions),
            db,
            Options.Create(new MaxioSettings
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "test-family"
            }),
            new MaxioWriteGuard());

        var result = await service.SubscribeAsync(
            new BillingUser("immutable-user-id", "demo@example.com", "Demo", "Customer"),
            "test-plan",
            CancellationToken.None);

        Assert.True(result.InProgress);
        Assert.Equal("processing", result.State);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(0, handler.SubscriptionPostCount);
    }

    [Fact]
    public async Task SubscribeSendsStableHandleReferenceAndRemittanceCollection()
    {
        var handler = new SubscriptionFlowHandler();
        using var httpClient = new HttpClient(handler);
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new CatalogContext(dbOptions);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        });
        var service = new MaxioSubscriptionBillingService(client, db, settings, new MaxioWriteGuard());

        var result = await service.SubscribeAsync(
            new BillingUser("immutable-user-id", "demo@example.com", "Demo", "Customer"),
            "test-plan",
            CancellationToken.None);

        Assert.Equal(7001, result.SubscriptionId);
        Assert.Equal("test-plan", result.ProductHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(1, handler.SubscriptionPostCount);
        Assert.Equal("test-plan", handler.PostedProductHandle);
        Assert.Equal("remittance", handler.PostedCollectionMethod);
        Assert.StartsWith("eshop-s-", handler.PostedReference);
    }

    private sealed class SubscriptionFlowHandler : HttpMessageHandler
    {
        private int _requestNumber;
        public int RequestCount => _requestNumber;
        public int SubscriptionPostCount { get; private set; }
        public string? PostedProductHandle { get; private set; }
        public string? PostedCollectionMethod { get; private set; }
        public string? PostedReference { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestNumber);
            return requestNumber switch
            {
                1 => Json(HttpStatusCode.OK,
                    """[{"product_family":{"id":101,"handle":"test-family"}}]"""),
                2 => Json(HttpStatusCode.OK,
                    """[{"product":{"id":201,"handle":"test-plan","name":"Test Plan","price_in_cents":1000,"interval":1,"interval_unit":"month"}}]"""),
                3 => Json(HttpStatusCode.NotFound, "{}"),
                4 => CustomerResponse(request),
                5 => await SubscriptionResponseAsync(request, cancellationToken),
                _ => throw new InvalidOperationException($"Unexpected Maxio request #{requestNumber}.")
            };
        }

        private static HttpResponseMessage CustomerResponse(HttpRequestMessage request)
        {
            var reference = QueryValue(request.RequestUri!, "reference");
            return Json(HttpStatusCode.OK,
                JsonSerializer.Serialize(new
                {
                    customer = new
                    {
                        id = 301,
                        first_name = "Demo",
                        last_name = "Customer",
                        email = "demo@example.com",
                        reference
                    }
                }));
        }

        private async Task<HttpResponseMessage> SubscriptionResponseAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SubscriptionPostCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var subscription = json.RootElement.GetProperty("subscription");
            PostedProductHandle = subscription.GetProperty("product_handle").GetString();
            PostedCollectionMethod = subscription.GetProperty("payment_collection_method").GetString();
            PostedReference = subscription.GetProperty("reference").GetString();

            return Json(HttpStatusCode.Created, JsonSerializer.Serialize(new
            {
                subscription = new
                {
                    id = 7001,
                    reference = PostedReference,
                    state = "active",
                    product_price_in_cents = 1000,
                    currency = "USD",
                    current_period_ends_at = "2026-09-27T00:00:00Z",
                    product = new { handle = "test-plan", name = "Test Plan" }
                }
            }));
        }

        private static string QueryValue(Uri uri, string name)
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.Ordinal))
                {
                    return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                }
            }

            throw new InvalidOperationException($"Missing query parameter '{name}'.");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
