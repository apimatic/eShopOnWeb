#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Drives <see cref="MaxioBillingGateway"/> through the real Maxio SDK with a stubbed HTTP handler: no network access.
/// </summary>
public class MaxioBillingGatewayTests
{
    private static readonly MaxioSettings s_settings = new()
    {
        ApiKey = "test-api-key-not-a-secret",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family"
    };

    private static MaxioBillingGateway CreateGateway(StubHandler handler, MaxioSettings? settings = null,
        Func<MaxioAdvancedBillingClientOptions, MaxioAdvancedBillingClientOptions>? tune = null)
    {
        settings ??= s_settings;
        var options = MaxioServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance);
        options = tune?.Invoke(options) ?? options;
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        return new MaxioBillingGateway(client, Options.Create(settings), NullLogger<MaxioBillingGateway>.Instance);
    }

    private static string Product(int id, string handle, long priceInCents, string? archivedAt = null) =>
        "{\"product\":{" +
        $"\"id\":{id},\"name\":\"{handle} name\",\"handle\":\"{handle}\",\"price_in_cents\":{priceInCents}," +
        "\"interval\":1,\"interval_unit\":\"month\"," +
        $"\"archived_at\":{(archivedAt is null ? "null" : $"\"{archivedAt}\"")}" +
        "}}";

    private const string SubscriptionJson =
        """{"subscription":{"id":555,"state":"active","reference":"eshop-sub-abc","product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-11-06T12:00:00Z","activated_at":"2026-10-06T12:00:00Z","created_at":"2026-10-06T12:00:00Z","product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"}}}""";

    [Fact]
    public async Task ListsPlansOfConfiguredFamilyByHandleAndSkipsArchived()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            $"[{Product(1, "eshop-pro", 29900)},{Product(2, "basic-plan", 2900)},{Product(3, "old-plan", 100, "2025-01-01T00:00:00Z")}]"));

        var catalog = await CreateGateway(handler).ListPlansAsync(default);

        Assert.False(catalog.IsTruncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        Assert.Equal(29900, catalog.Plans[0].PriceInCents);
        Assert.Equal("month", catalog.Plans[0].IntervalUnit);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("test-site.chargify.com", request.RequestUri!.Host);
        Assert.Contains("/product_families/handle%3Atest-family/products.json", request.RequestUri.AbsoluteUri);
        Assert.Contains("per_page=200", request.RequestUri.Query);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task StopsAtPageCapAndReportsTruncation()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, MaxioBillingGateway.PlanPageSize)
            .Select(i => Product(i, $"plan-{i}", 100))) + "]";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, fullPage));

        var catalog = await CreateGateway(handler).ListPlansAsync(default);

        Assert.True(catalog.IsTruncated);
        Assert.Equal(MaxioBillingGateway.MaxPlanPages, handler.Requests.Count);
        Assert.Contains($"page={MaxioBillingGateway.MaxPlanPages}", handler.Requests.Last().RequestUri!.Query);
    }

    [Fact]
    public async Task UsesBaseUrlVerbatimWhenConfigured()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key-not-a-secret",
            ProductFamilyHandle = "test-family",
            Subdomain = "ignored-site",
            BaseUrl = "http://maxio-mock.test:8080/billing"
        };
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "[]"));

        await CreateGateway(handler, settings).ListPlansAsync(default);

        Assert.StartsWith("http://maxio-mock.test:8080/billing/product_families/", handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task UnknownProductFamilyIsReportedAsUnavailable()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.NotFound, "\"Not Found\""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateGateway(handler).ListPlansAsync(default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(404, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task CreateSubscriptionSendsPlanCustomerReferenceAndInvoiceCollection()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.Created, SubscriptionJson));

        var created = await CreateGateway(handler).CreateSubscriptionAsync(
            new NewBillingSubscription(77, "eshop-pro", "eshop-sub-abc"), default);

        Assert.Equal(555, created.Id);
        Assert.Equal("active", created.State);
        Assert.Equal("eshop-pro", created.PlanHandle);
        Assert.Equal(29900, created.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero), created.NextBillingAt);
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
        Assert.EndsWith("/subscriptions.json", handler.Requests.Single().RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Bodies.Single()!);
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(77, subscription.GetProperty("customer_id").GetInt32());
        Assert.Equal("eshop-sub-abc", subscription.GetProperty("reference").GetString());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task RejectedSubscriptionCarriesMaxioErrors()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateGateway(handler)
            .CreateSubscriptionAsync(new NewBillingSubscription(77, "eshop-pro", "eshop-sub-abc"), default));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.False(ex.OutcomeUnknown);
        Assert.Contains("No payment method was on file for the $299.00 balance", ex.Errors);
    }

    [Fact]
    public async Task ConnectionFailureOnCreateIsUnknownOutcomeAndNeverResent()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateGateway(handler)
            .CreateSubscriptionAsync(new NewBillingSubscription(77, "eshop-pro", "eshop-sub-abc"), default));

        Assert.Equal(BillingFailureKind.NoResponse, ex.Kind);
        Assert.True(ex.OutcomeUnknown);
        Assert.StartsWith("Maxio did not respond", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ServerErrorOnCreateIsUnknownOutcomeAndNeverResent()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateGateway(handler)
            .CreateSubscriptionAsync(new NewBillingSubscription(77, "eshop-pro", "eshop-sub-abc"), default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.True(ex.OutcomeUnknown);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UnreadableSuccessBodyOnCreateIsUnknownOutcome()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.Created, """{"subscription":"not-an-object"}"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateGateway(handler)
            .CreateSubscriptionAsync(new NewBillingSubscription(77, "eshop-pro", "eshop-sub-abc"), default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.True(ex.OutcomeUnknown);
        Assert.DoesNotContain("MaxioAdvancedBilling", ex.Message);
    }

    [Fact]
    public async Task HungMaxioSurfacesAsDidNotRespond()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubHandler.Json(HttpStatusCode.OK, "[]");
        });
        var gateway = CreateGateway(handler, tune: o =>
        {
            o.Retry = RetryOptions.Default() with { MaxRetries = 0, Timeout = TimeSpan.FromMilliseconds(100) };
            return o;
        });

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => gateway.ListPlansAsync(default));

        Assert.Equal(BillingFailureKind.NoResponse, ex.Kind);
        Assert.StartsWith("Maxio did not respond", ex.Message);
    }

    [Fact]
    public async Task CallerCancellationIsNotTranslated()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubHandler.Json(HttpStatusCode.OK, "[]");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateGateway(handler).ListPlansAsync(cts.Token));
    }

    [Fact]
    public async Task CustomerLookupMissReturnsNull()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.NotFound, "{}"));

        Assert.Null(await CreateGateway(handler).FindCustomerByReferenceAsync("eshop-user-1", default));
        Assert.Contains("reference=eshop-user-1", handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task RejectedCredentialsAreNotPassedThroughAsCallerError()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateGateway(handler).FindCustomerByReferenceAsync("eshop-user-1", default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(401, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task CreateCustomerSendsReferenceAndContactDetails()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.Created,
            """{"customer":{"id":31,"reference":"eshop-user-1","email":"demo@example.com"}}"""));

        var account = await CreateGateway(handler).CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-1", "Demo", "User", "demo@example.com"), default);

        Assert.Equal(31, account.Id);
        using var body = JsonDocument.Parse(handler.Bodies.Single()!);
        var customer = body.RootElement.GetProperty("customer");
        Assert.Equal("eshop-user-1", customer.GetProperty("reference").GetString());
        Assert.Equal("demo@example.com", customer.GetProperty("email").GetString());
        Assert.Equal("Demo", customer.GetProperty("first_name").GetString());
        Assert.Equal("User", customer.GetProperty("last_name").GetString());
    }

    [Fact]
    public async Task RejectedCustomerCarriesMaxioErrors()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference must be unique"]}"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateGateway(handler)
            .CreateCustomerAsync(new NewBillingCustomer("eshop-user-1", "Demo", "User", "demo@example.com"), default));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("Reference must be unique", ex.Errors);
    }

    [Fact]
    public async Task SubscriptionLookupMissReturnsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await CreateGateway(handler).FindSubscriptionByReferenceAsync("eshop-sub-abc", default));
    }

    [Fact]
    public async Task ListsCustomerSubscriptions()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{SubscriptionJson}]"));

        var subscriptions = await CreateGateway(handler).ListCustomerSubscriptionsAsync(77, default);

        Assert.Equal(555, Assert.Single(subscriptions).Id);
        Assert.EndsWith("/customers/77/subscriptions.json", handler.Requests.Single().RequestUri!.AbsolutePath);
    }
}
