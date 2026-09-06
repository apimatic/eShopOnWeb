using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingGatewayTests
{
    private const string SiteJson = """
        {"site":{"id":1,"name":"CP","subdomain":"test-site","currency":"USD",
        "relationship_invoicing_enabled":true,"default_payment_collection_method":"automatic","test":true}}
        """;

    private const string ProductsJson = """
        [{"product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,
          "interval_unit":"month","require_credit_card":false,"product_price_point_name":"Original",
          "archived_at":null,"product_family":{"id":3,"handle":"eshop-subscribe","name":"eShopSubscribe"}}},
         {"product":{"id":11,"handle":"retired-plan","name":"Retired","price_in_cents":100,"interval":1,
          "interval_unit":"month","require_credit_card":false,"archived_at":"2020-01-01T00:00:00Z"}}]
        """;

    private readonly StubHttpMessageHandler _handler = new();

    private MaxioBillingGateway CreateGateway(MaxioSettings? settings = null)
    {
        settings ??= new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            RetryBaseDelayMilliseconds = 0
        };

        var httpClient = new HttpClient(_handler) { BaseAddress = settings.ResolveBaseAddress() };

        return new MaxioBillingGateway(
            httpClient,
            Options.Create(settings),
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<IAppLogger<MaxioBillingGateway>>());
    }

    [Fact]
    public async Task ListPlansAsyncAsksTheConfiguredFamilyByHandleAndSkipsArchivedProducts()
    {
        _handler.Respond(HttpStatusCode.OK, SiteJson).Respond(HttpStatusCode.OK, ProductsJson);

        var plans = await CreateGateway().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal("USD", plan.Currency);
        Assert.False(plan.RequiresPaymentMethod);

        Assert.Contains("product_families/handle%3Aeshop-subscribe/products.json", _handler.Requests[1].Uri);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsyncTreatsA404AsNoSuchCustomer()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""");

        Assert.Null(await CreateGateway().FindCustomerByReferenceAsync("eshoponweb-nobody@example.com"));
    }

    [Fact]
    public async Task CreateCustomerAsyncReportsATakenReferenceAsAConflict()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        await Assert.ThrowsAsync<BillingConflictException>(() => CreateGateway().CreateCustomerAsync(new NewBillingCustomer
        {
            Reference = "eshoponweb-demouser@microsoft.com",
            FirstName = "Demo",
            LastName = "User",
            Email = "demouser@microsoft.com"
        }));
    }

    [Fact]
    public async Task CreateCustomerAsyncReportsOtherRejectionsAsValidationFailures()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":["Last name: cannot be blank."]}""");

        var exception = await Assert.ThrowsAsync<BillingValidationException>(() => CreateGateway().CreateCustomerAsync(
            new NewBillingCustomer { Reference = "r", FirstName = "Demo", LastName = "", Email = "d@example.com" }));

        Assert.Contains("Last name: cannot be blank.", exception.Errors);
    }

    [Fact]
    public async Task CreateSubscriptionAsyncBillsByInvoiceBecauseNoPaymentMethodIsCaptured()
    {
        _handler.Respond(HttpStatusCode.OK, SiteJson)
            .Respond(HttpStatusCode.Created, """{"subscription":{"id":5,"state":"active"}}""");

        await CreateGateway().CreateSubscriptionAsync(new NewSubscription
        {
            CustomerId = 42,
            PlanHandle = "eshop-pro",
            UniquenessToken = "token-abc"
        });

        var body = _handler.Requests.Last().Body;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":42", body);
        // The site runs Relationship Invoicing, so its invoice-style collection method is "remittance".
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        // The token sits beside the subscription object, which is where duplicate prevention reads it.
        Assert.Contains("\"uniqueness_token\":\"token-abc\"", body);
    }

    [Fact]
    public async Task CreateSubscriptionAsyncHonoursAConfiguredPaymentCollectionMethod()
    {
        _handler.Respond(HttpStatusCode.OK, SiteJson)
            .Respond(HttpStatusCode.Created, """{"subscription":{"id":5,"state":"active"}}""");

        await CreateGateway(new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            PaymentCollectionMethod = "automatic",
            RetryBaseDelayMilliseconds = 0
        }).CreateSubscriptionAsync(new NewSubscription { CustomerId = 42, PlanHandle = "eshop-pro" });

        Assert.Contains("\"payment_collection_method\":\"automatic\"", _handler.Requests.Last().Body);
    }

    [Fact]
    public async Task GetSiteAsyncIsReadOnceAndThenServedFromCache()
    {
        _handler.Respond(HttpStatusCode.OK, SiteJson);
        var gateway = CreateGateway();

        var first = await gateway.GetSiteAsync();
        var second = await gateway.GetSiteAsync();

        Assert.Same(first, second);
        Assert.Single(_handler.Requests);
        Assert.True(first.RelationshipInvoicingEnabled);
        Assert.Equal("remittance", first.InvoicePaymentCollectionMethod);
    }

    [Fact]
    public async Task RetriesWhenMaxioThrottlesAndSucceedsOnTheNextAttempt()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, """{"errors":["slow down"]}""")
            .Respond(HttpStatusCode.OK, SiteJson);

        var site = await CreateGateway().GetSiteAsync();

        Assert.Equal("USD", site.Currency);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAsUnavailableWhenMaxioKeepsFailing()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            MaxRetries = 2,
            RetryBaseDelayMilliseconds = 0
        };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            _handler.Respond(HttpStatusCode.BadGateway, """{"errors":["upstream is down"]}""");
        }

        await Assert.ThrowsAsync<BillingUnavailableException>(() => CreateGateway(settings).GetSiteAsync());
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task ReportsATransportFailureAsUnavailableRatherThanLettingItEscape()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            MaxRetries = 0
        };
        _handler.Throw(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<BillingUnavailableException>(() => CreateGateway(settings).GetSiteAsync());
    }

    [Fact]
    public async Task ExplainsThatCredentialsWereRejectedWithoutEchoingThem()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, string.Empty);

        var exception = await Assert.ThrowsAsync<BillingException>(() => CreateGateway().GetSiteAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.DoesNotContain("not-a-real-key", exception.Message);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsAsyncMapsThePlanAndNextBillingDate()
    {
        _handler.Respond(HttpStatusCode.OK, SiteJson).Respond(HttpStatusCode.OK, """
            [{"subscription":{"id":9,"state":"active","product_price_in_cents":29900,"balance_in_cents":29900,
              "next_assessment_at":"2026-10-06T14:57:35+05:00","current_period_started_at":"2026-09-06T14:57:35+05:00",
              "current_period_ends_at":"2026-10-06T14:57:35+05:00","activated_at":"2026-09-06T14:57:36+05:00",
              "created_at":"2026-09-06T14:57:35+05:00",
              "product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"},
              "customer":{"id":42,"reference":"eshoponweb-demouser@microsoft.com"}}}]
            """);

        var subscriptions = await CreateGateway().ListCustomerSubscriptionsAsync(42);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(9, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(42, subscription.CustomerId);
        Assert.True(subscription.IsLive);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 14, 57, 35, TimeSpan.FromHours(5)), subscription.NextBillingAt);
    }
}
