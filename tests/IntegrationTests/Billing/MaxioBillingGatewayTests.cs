#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioBillingGatewayTests
{
    private const string Family = "test-family";
    private const string BaseUrl = "https://maxio.example.test";

    private static MaxioBillingGateway CreateGateway(MaxioStubHandler handler)
    {
        var settings = new MaxioOptions
        {
            ApiKey = "test-key",
            BaseUrl = BaseUrl,
            ProductFamilyHandle = Family,
        };
        // The production options (retry policy, auth, base URL) — only the transport is stubbed.
        var clientOptions = MaxioServiceCollectionExtensions.BuildClientOptions(settings, NullLoggerFactory.Instance,
            NullLogger.Instance, TimeProvider.System);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);
        return new MaxioBillingGateway(client, Options.Create(settings), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioBillingGateway>.Instance);
    }

    private static bool Is(HttpRequestMessage request, HttpMethod method, string path) =>
        request.Method == method && request.RequestUri!.AbsolutePath == path;

    [Fact]
    public async Task CreateSubscription_ConnectionFails_SettlesViaFindSubscription()
    {
        var handler = new MaxioStubHandler(request =>
        {
            if (Is(request, HttpMethod.Post, "/subscriptions.json"))
            {
                throw new HttpRequestException("connection reset after send");
            }
            if (Is(request, HttpMethod.Get, "/subscriptions/lookup.json"))
            {
                return MaxioStubHandler.Json(HttpStatusCode.OK, MaxioStubHandler.Subscription(42, "ref-1", 7, "eshop-pro", Family));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(7, "eshop-pro", "ref-1");

        Assert.False(result.IsOutcomeUnknown);
        Assert.Equal(42, result.Subscription!.Id);
        // The write was sent exactly once (never resent by the SDK), then settled by reference.
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Contains(handler.Requests, r => Is(r, HttpMethod.Get, "/subscriptions/lookup.json") && r.RequestUri!.Query.Contains("reference=ref-1"));
    }

    [Fact]
    public async Task CreateSubscription_ConnectionFailsAndNotFound_ReturnsUnknownOutcome()
    {
        var handler = new MaxioStubHandler(request =>
        {
            if (Is(request, HttpMethod.Post, "/subscriptions.json"))
            {
                throw new HttpRequestException("connection reset after send");
            }
            return MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(7, "eshop-pro", "ref-unknown");

        Assert.True(result.IsOutcomeUnknown);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CreateSubscription_SendsHandleCustomerReferenceAndRemittance()
    {
        var handler = new MaxioStubHandler(_ =>
            MaxioStubHandler.Json(HttpStatusCode.Created, MaxioStubHandler.Subscription(42, "ref-2", 7, "eshop-pro", Family)));
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(7, "eshop-pro", "ref-2");

        Assert.Equal(42, result.Subscription!.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);
        var body = handler.Bodies.Single();
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":7", body);
        Assert.Contains("\"reference\":\"ref-2\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.StartsWith(BaseUrl, handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateSubscription_Rejected_ThrowsRejectedWithProviderErrors()
    {
        var handler = new MaxioStubHandler(request => Is(request, HttpMethod.Post, "/subscriptions.json")
            ? MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product must be specified."]}""")
            : MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}"));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => gateway.CreateSubscriptionAsync(7, "eshop-pro", "ref-3"));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("Product must be specified.", ex.ProviderErrors);
    }

    [Fact]
    public async Task CreateSubscription_RejectedButReferenceExists_ReturnsExisting()
    {
        var handler = new MaxioStubHandler(request => Is(request, HttpMethod.Post, "/subscriptions.json")
            ? MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference has already been taken"]}""")
            : MaxioStubHandler.Json(HttpStatusCode.OK, MaxioStubHandler.Subscription(43, "ref-4", 7, "eshop-pro", Family)));
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(7, "eshop-pro", "ref-4");

        Assert.Equal(43, result.Subscription!.Id);
    }

    [Fact]
    public async Task CreateCustomer_ConnectionFails_SettlesViaReadByReference()
    {
        var lookups = 0;
        var handler = new MaxioStubHandler(request =>
        {
            if (Is(request, HttpMethod.Get, "/customers/lookup.json"))
            {
                // Absent before the create; present when re-read after the failed connection.
                return lookups++ == 0
                    ? MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}")
                    : MaxioStubHandler.Json(HttpStatusCode.OK, MaxioStubHandler.Customer(77, "cust-ref"));
            }
            if (Is(request, HttpMethod.Post, "/customers.json"))
            {
                throw new HttpRequestException("connection reset after send");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var gateway = CreateGateway(handler);

        var id = await gateway.EnsureCustomerAsync(new BillingCustomer("cust-ref", "a@b.test", "A", "B"));

        Assert.Equal(77, id);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task EnsureCustomer_ExistingReference_DoesNotCreate()
    {
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.OK, MaxioStubHandler.Customer(5, "cust-ref")));
        var gateway = CreateGateway(handler);

        var id = await gateway.EnsureCustomerAsync(new BillingCustomer("cust-ref", "a@b.test", "A", "B"));

        Assert.Equal(5, id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GetPlans_ListsUnarchivedProductsOfConfiguredFamilyByHandle()
    {
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.OK,
            $"[{MaxioStubHandler.Product(1, "eshop-pro", 29900)},{MaxioStubHandler.Product(2, "basic-plan", 2900)},{MaxioStubHandler.Product(3, "old-plan", 100, "2025-01-01T00:00:00Z")}]"));
        var gateway = CreateGateway(handler);

        var catalog = await gateway.GetPlansAsync();

        Assert.False(catalog.IsTruncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        Assert.Equal(29900, catalog.Plans[0].PriceInCents);
        Assert.Equal("month", catalog.Plans[0].IntervalUnit);
        var uri = handler.Requests.Single().RequestUri!;
        Assert.Equal($"/product_families/handle:{Family}/products.json", Uri.UnescapeDataString(uri.AbsolutePath));
        Assert.Contains("per_page=200", uri.Query);
    }

    [Fact]
    public async Task GetPlans_StopsAtPageCap_ReportsTruncated()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 200).Select(i => MaxioStubHandler.Product(i, $"plan-{i}", 100))) + "]";
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.OK, fullPage));
        var gateway = CreateGateway(handler);

        var catalog = await gateway.GetPlansAsync();

        Assert.True(catalog.IsTruncated);
        Assert.Equal(10, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPlans_UnknownFamily_ThrowsMisconfigured()
    {
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.NotFound, "\"Not Found\""));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => gateway.GetPlansAsync());

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
    }

    [Fact]
    public async Task FindSubscription_NotFound_ReturnsNull()
    {
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}"));
        var gateway = CreateGateway(handler);

        Assert.Null(await gateway.FindSubscriptionAsync("missing"));
    }

    [Fact]
    public async Task ReadCalls_Unauthorized_ThrowMisconfigured()
    {
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => gateway.ListCustomerSubscriptionsAsync(1));

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
        Assert.NotNull(handler.Requests.Single().Headers.Authorization);
    }
}
