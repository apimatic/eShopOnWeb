using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Pins the Maxio wire contract: the paths we call, the bodies we send and the fields we
/// read. The canned responses are trimmed copies of real Advanced Billing sandbox payloads.
/// </summary>
public class MaxioBillingGatewayTests
{
    private const string SiteJson = """{"site":{"id":93063,"name":"Demo","subdomain":"demo-site","currency":"USD"}}""";

    private const string ProductsJson = """
    [
      {"product":{"id":7131000,"name":"Basic Plan","handle":"basic-plan","description":null,"price_in_cents":2900,
        "interval":1,"interval_unit":"month","require_credit_card":false,"request_credit_card":true,
        "trial_interval":null,"trial_interval_unit":null,"archived_at":null,
        "product_family":{"id":3026731,"name":"eShopSubscribe","handle":"eshop-subscribe"}}},
      {"product":{"id":7130999,"name":"Pro Plan","handle":"eshop-pro","description":null,"price_in_cents":29900,
        "interval":1,"interval_unit":"month","require_credit_card":false,"request_credit_card":true,
        "trial_interval":null,"trial_interval_unit":null,"archived_at":null,
        "product_family":{"id":3026731,"name":"eShopSubscribe","handle":"eshop-subscribe"}}},
      {"product":{"id":7130998,"name":"Retired Plan","handle":"retired","price_in_cents":100,
        "interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":"2026-01-01T00:00:00+05:00",
        "product_family":{"id":3026731,"name":"eShopSubscribe","handle":"eshop-subscribe"}}}
    ]
    """;

    private const string CustomerJson = """
    {"customer":{"first_name":"Demouser","last_name":"Customer","email":"demouser@microsoft.com",
      "reference":"eshop:demouser@microsoft.com","id":98838143,"created_at":"2026-09-06T14:56:10+05:00"}}
    """;

    private const string SubscriptionJson = """
    {"subscription":{"id":94209546,"state":"active","balance_in_cents":29900,"product_price_in_cents":29900,
      "currency":"USD","payment_collection_method":"remittance","reference":null,
      "current_period_started_at":"2026-09-06T14:56:11+05:00",
      "current_period_ends_at":"2026-10-06T14:56:11+05:00",
      "next_assessment_at":"2026-10-06T14:56:11+05:00",
      "activated_at":"2026-09-06T14:56:13+05:00","canceled_at":null,
      "created_at":"2026-09-06T14:56:11+05:00",
      "customer":{"id":98838143,"reference":"eshop:demouser@microsoft.com","email":"demouser@microsoft.com"},
      "product":{"id":7130999,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
        "interval":1,"interval_unit":"month"}}}
    """;

    private static (MaxioBillingGateway Gateway, StubHttpMessageHandler Handler) CreateGateway(
        Func<HttpRequestMessage, (HttpStatusCode, string)> respond,
        MaxioOptions? options = null)
    {
        options ??= new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "demo-site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        var handler = new StubHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler) { BaseAddress = options.ResolveBaseAddress() };

        var gateway = new MaxioBillingGateway(
            httpClient,
            Options.Create(options),
            NullLogger<MaxioBillingGateway>.Instance);

        return (gateway, handler);
    }

    private static (HttpStatusCode, string) Catalog(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/site.json" => (HttpStatusCode.OK, SiteJson),
            "/product_families/handle:eshop-subscribe/products.json" => (HttpStatusCode.OK, ProductsJson),
            _ => (HttpStatusCode.NotFound, """{"errors":["Not found"]}""")
        };

    [Fact]
    public async Task ListPlansAddressesTheFamilyByHandleAndProjectsTheProducts()
    {
        var (gateway, handler) = CreateGateway(Catalog);

        var plans = await gateway.ListPlansAsync();

        Assert.Contains(handler.Requests, r =>
            r.Method == HttpMethod.Get &&
            r.Url == "https://demo-site.chargify.com/product_families/handle:eshop-subscribe/products.json");

        // Archived products are not on offer, and the cheapest plan comes first.
        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task FindPlanIsScopedToTheConfiguredFamily()
    {
        var (gateway, _) = CreateGateway(Catalog);

        Assert.NotNull(await gateway.FindPlanAsync("eshop-pro"));
        Assert.Null(await gateway.FindPlanAsync("some-other-family-plan"));
    }

    [Fact]
    public async Task ListPlansSurvivesTheSiteCurrencyBeingUnavailable()
    {
        var (gateway, _) = CreateGateway(request => request.RequestUri!.AbsolutePath == "/site.json"
            ? (HttpStatusCode.InternalServerError, "boom")
            : Catalog(request));

        var plans = await gateway.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.Null(plan.Currency));
    }

    [Fact]
    public async Task FindCustomerByReferenceLooksUpTheExactReferenceAndMapsNullFor404()
    {
        var (gateway, handler) = CreateGateway(request =>
            request.RequestUri!.Query.Contains("eshop%3Aknown", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, CustomerJson)
                : (HttpStatusCode.NotFound, """{"errors":["Customer not found"]}"""));

        var found = await gateway.FindCustomerByReferenceAsync("eshop:known@example.com");
        var missing = await gateway.FindCustomerByReferenceAsync("eshop:nobody@example.com");

        Assert.Equal("https://demo-site.chargify.com/customers/lookup.json?reference=eshop%3Aknown%40example.com",
            handler.Requests[0].Url);
        Assert.NotNull(found);
        Assert.Equal(98838143, found!.Id);
        Assert.Equal("demouser@microsoft.com", found.Email);
        Assert.Null(missing);
    }

    [Fact]
    public async Task CreateCustomerPostsTheDocumentedEnvelope()
    {
        var (gateway, handler) = CreateGateway(_ => (HttpStatusCode.Created, CustomerJson));

        await gateway.CreateCustomerAsync(new NewBillingCustomer
        {
            Reference = "eshop:demouser@microsoft.com",
            Email = "demouser@microsoft.com",
            FirstName = "Demouser",
            LastName = "Customer"
        });

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://demo-site.chargify.com/customers.json", request.Url);

        using var document = JsonDocument.Parse(request.Body!);
        var customer = document.RootElement.GetProperty("customer");
        Assert.Equal("Demouser", customer.GetProperty("first_name").GetString());
        Assert.Equal("Customer", customer.GetProperty("last_name").GetString());
        Assert.Equal("demouser@microsoft.com", customer.GetProperty("email").GetString());
        Assert.Equal("eshop:demouser@microsoft.com", customer.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task CreateSubscriptionPostsTheDocumentedEnvelopeAndMapsTheResponse()
    {
        var (gateway, handler) = CreateGateway(_ => (HttpStatusCode.Created, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(new NewSubscription
        {
            CustomerId = 98838143,
            PlanHandle = "eshop-pro",
            PaymentCollectionMethod = PaymentCollectionMethods.Remittance,
            Reference = "eshop-sub-abc"
        });

        var request = handler.Requests.Single();
        Assert.Equal("https://demo-site.chargify.com/subscriptions.json", request.Url);

        using var document = JsonDocument.Parse(request.Body!);
        var body = document.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
        Assert.Equal(98838143, body.GetProperty("customer_id").GetInt32());
        Assert.Equal("remittance", body.GetProperty("payment_collection_method").GetString());
        Assert.Equal("eshop-sub-abc", body.GetProperty("reference").GetString());

        Assert.Equal(94209546, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(98838143, subscription.CustomerId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal("month", subscription.IntervalUnit);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T14:56:11+05:00"), subscription.NextBillingAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T14:56:11+05:00"), subscription.CurrentPeriodEndsAt);
        Assert.Equal(29900, subscription.BalanceInCents);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
        Assert.True(subscription.IsLive);
    }

    [Fact]
    public async Task OmitsTheReferenceWhenTheCallerSuppliesNone()
    {
        var (gateway, handler) = CreateGateway(_ => (HttpStatusCode.Created, SubscriptionJson));

        await gateway.CreateSubscriptionAsync(new NewSubscription
        {
            CustomerId = 1,
            PlanHandle = "eshop-pro",
            PaymentCollectionMethod = PaymentCollectionMethods.Remittance
        });

        using var document = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.False(document.RootElement.GetProperty("subscription").TryGetProperty("reference", out _));
    }

    [Fact]
    public async Task FlagsTheTakenReferenceFailureSoCallersCanRecover()
    {
        var (gateway, _) = CreateGateway(_ => (HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            gateway.CreateCustomerAsync(new NewBillingCustomer
            {
                Reference = "eshop:demouser@microsoft.com",
                Email = "demouser@microsoft.com",
                FirstName = "Demouser",
                LastName = "Customer"
            }));

        Assert.True(exception.IsDuplicateReference);
        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("must be unique", exception.Errors.Single());
    }

    [Fact]
    public async Task ReadsFieldScopedValidationErrors()
    {
        var (gateway, _) = CreateGateway(_ => (HttpStatusCode.UnprocessableEntity,
            """{"errors":{"product_handle":["is not valid"]}}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            gateway.CreateSubscriptionAsync(new NewSubscription
            {
                CustomerId = 1,
                PlanHandle = "nope",
                PaymentCollectionMethod = PaymentCollectionMethods.Remittance
            }));

        Assert.False(exception.IsDuplicateReference);
        Assert.Equal("product_handle: is not valid", exception.Errors.Single());
    }

    [Fact]
    public async Task ReportsAnUnreachableBillingSystemWithoutAStatusCode()
    {
        var handler = new ThrowingHandler();
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "demo-site", ProductFamilyHandle = "f", MaxRetryAttempts = 0 };
        var gateway = new MaxioBillingGateway(
            new HttpClient(handler) { BaseAddress = options.ResolveBaseAddress() },
            Options.Create(options),
            NullLogger<MaxioBillingGateway>.Instance);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => gateway.ListPlansAsync());

        Assert.Null(exception.StatusCode);
        Assert.Contains("could not be reached", exception.Message);
    }

    [Fact]
    public async Task TreatsAMissingProductFamilyAsAFailureRatherThanAnEmptyCatalogue()
    {
        var (gateway, _) = CreateGateway(request => request.RequestUri!.AbsolutePath == "/site.json"
            ? (HttpStatusCode.OK, SiteJson)
            : (HttpStatusCode.NotFound, """{"errors":["Product family not found"]}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => gateway.ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message);
    }

    [Fact]
    public async Task SendsEveryRequestToAnExplicitBaseUrlVerbatim()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "ignored-when-base-url-is-set",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://billing.internal.example.com/maxio"
        };

        var (gateway, handler) = CreateGateway(
            request => request.RequestUri!.AbsolutePath.EndsWith("site.json", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, SiteJson)
                : (HttpStatusCode.OK, ProductsJson),
            options);

        await gateway.ListPlansAsync();

        Assert.Contains(handler.Requests, r =>
            r.Url == "https://billing.internal.example.com/maxio/product_families/handle:eshop-subscribe/products.json");
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
