using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.SubscriptionBilling;

public class SubscriptionBillingSettingsTests
{
    [Fact]
    public void BaseUrl_WhenSet_IsUsedVerbatimAsApiBase()
    {
        var settings = new SubscriptionBillingSettings
        {
            ApiKey = "k",
            Subdomain = "ignored",
            ProductFamilyHandle = "f",
            BaseUrl = "https://custom.example.com/",
        };

        Assert.Equal("https://custom.example.com", settings.GetApiBaseUrl());
    }

    [Fact]
    public void Subdomain_DerivesTheDocumentedChargifyHost()
    {
        var settings = new SubscriptionBillingSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "f",
        };

        Assert.Equal("https://cp-exp-4.chargify.com", settings.GetApiBaseUrl());
    }

    [Fact]
    public void EnsureConfigured_ReportsMissingKeysWithoutEchoingSecrets()
    {
        var settings = new SubscriptionBillingSettings { ApiKey = " " };

        var exception = Assert.Throws<SubscriptionBillingException>(settings.EnsureConfigured);

        Assert.Equal(SubscriptionBillingErrorKind.Configuration, exception.Kind);
        Assert.Contains("Maxio:", exception.Message);
    }

    [Fact]
    public void EnsureConfigured_SucceedsForACompleteSetup()
    {
        var settings = new SubscriptionBillingSettings
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "f",
        };

        settings.EnsureConfigured();
    }
}

internal class StubHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, HttpResponseMessage> _responder = _ => new HttpResponseMessage(HttpStatusCode.OK);

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public int CallCount { get; private set; }

    public void RespondWith(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    public void RespondWith(HttpStatusCode status, string body) =>
        _responder = _ => new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responder(request);
    }
}

public class MaxioSubscriptionBillingClientTests
{
    private static MaxioSubscriptionBillingClient CreateClient(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        // Mirror what the production registration puts on the typed HttpClient.
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", "dGVzdC1rZXk6WA==");
        var options = Options.Create(new SubscriptionBillingSettings
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
        });
        return new MaxioSubscriptionBillingClient(client, options);
    }

    [Fact]
    public async Task GetPlansAsync_ParsesWrappedProductArray_AndSkipsArchivedAndHandleless()
    {
        var handler = new StubHandler();
        handler.RespondWith(HttpStatusCode.OK, """
            [
              {"product": {"id": 7131000, "name": "Basic Plan", "handle": "basic-plan", "description": "", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false, "archived_at": null}},
              {"product": {"id": 7130999, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false}},
              {"product": {"id": 1, "name": "Old", "handle": "old", "archived_at": "2020-01-01T00:00:00-05:00"}},
              {"product": {"id": 2, "name": "No handle", "handle": null}}
            ]
            """);

        var plans = await CreateClient(handler).GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(29900, plans[1].PriceInCents);
        Assert.Equal("https://acme.chargify.com/product_families/handle:eshop-subscribe/products.json", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("application/json", handler.LastRequest!.Headers.Accept.ToString());
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNull_On404()
    {
        var handler = new StubHandler();
        handler.RespondWith(HttpStatusCode.NotFound, "{\"errors\":[\"Resource not found\"]}");

        var customer = await CreateClient(handler).FindCustomerByReferenceAsync("someone@example.com");

        Assert.Null(customer);
        Assert.Equal("https://acme.chargify.com/customers/lookup.json?reference=someone%40example.com", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsProductHandleCustomerIdAndUniquenessToken()
    {
        var handler = new StubHandler();
        handler.RespondWith(HttpStatusCode.Created, """
            {"subscription": {"id": 94687991, "state": "active", "product_price_in_cents": 29900, "payment_collection_method": "remittance", "current_period_started_at": "2026-10-06 15:56:52 -0500", "current_period_ends_at": "2026-11-06T15:56:52-05:00", "next_assessment_at": "2026-11-06T15:56:52-05:00", "activated_at": null, "created_at": "2026-10-06T15:56:52-05:00", "customer": {"id": 99270764}, "product": {"id": 7130999, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month"}}}
            """);

        var subscription = await CreateClient(handler).CreateSubscriptionAsync(new NewBillingSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 99270764,
            PaymentCollectionMethod = "remittance",
            IdempotencyKey = "fixed-key",
        });

        Assert.Equal(94687991, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.NotNull(subscription.NextBillingDate);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.LastRequestBody);
        Assert.Contains("\"customer_id\":99270764", handler.LastRequestBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", handler.LastRequestBody);
        Assert.Contains("\"uniqueness_token\":\"fixed-key\"", handler.LastRequestBody);
        Assert.StartsWith("Basic ", handler.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesValidationError_AsRejected()
    {
        var handler = new StubHandler();
        handler.RespondWith(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            CreateClient(handler).CreateSubscriptionAsync(new NewBillingSubscription { ProductHandle = "x", CustomerId = 1 }));

        Assert.Equal(SubscriptionBillingErrorKind.Rejected, exception.Kind);
        Assert.Contains("No payment method was on file", exception.Message);
        Assert.Single(exception.Errors);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_MapsDuplicate_AsDuplicate()
    {
        var handler = new StubHandler();
        handler.RespondWith(HttpStatusCode.Conflict, """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""");

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            CreateClient(handler).CreateSubscriptionAsync(new NewBillingSubscription { ProductHandle = "x", CustomerId = 1 }));

        Assert.Equal(SubscriptionBillingErrorKind.Duplicate, exception.Kind);
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_ParsesWrappedSubscriptionArray()
    {
        var handler = new StubHandler();
        handler.RespondWith(HttpStatusCode.OK, """
            [
              {"subscription": {"id": 1, "state": "active", "product_price_in_cents": 29900, "created_at": "2026-10-06T15:56:52-05:00", "customer": {"id": 42}, "product": {"id": 2, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month"}}},
              {"subscription": {"id": 2, "state": "canceled", "product_price_in_cents": 2900, "created_at": "2026-09-01T10:00:00-05:00", "customer": {"id": 42}, "product": {"id": 3, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month"}}}
            ]
            """);

        var subscriptions = await CreateClient(handler).GetSubscriptionsForCustomerAsync(42);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("eshop-pro", subscriptions[0].PlanHandle);
        Assert.True(subscriptions[1].IsEndOfLife());
    }
}
