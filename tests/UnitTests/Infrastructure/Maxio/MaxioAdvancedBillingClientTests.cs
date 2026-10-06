using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAdvancedBillingClientTests
{
    private static MaxioSettings Settings => new MaxioSettings
    {
        ApiKey = "sandbox-key",
        Subdomain = "my-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
        private readonly Queue<HttpResponseMessage> _responses = new Queue<HttpResponseMessage>();

        public StubHandler Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static (MaxioAdvancedBillingClient client, StubHandler handler) CreateClient(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new System.Uri("https://my-site.chargify.com/") };
        return (new MaxioAdvancedBillingClient(http, Settings), handler);
    }

    [Fact]
    public async Task GetPlansParsesTheProductFamilyProductList()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, """
            [{"product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan","description":null,"price_in_cents":29900,
              "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
              "product_family":{"id":3026730,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}]
            """);
        var (client, _) = CreateClient(handler);

        var plans = await client.GetPlansAsync("eshop-subscribe");

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(plan.RequiresPaymentMethod);
        Assert.False(plan.IsArchived);
        Assert.Equal("eshop-subscribe", plan.FamilyHandle);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", handler.Requests.Single().RequestUri!.PathAndQuery.Replace("%3A", ":"));
    }

    [Fact]
    public async Task EnsureCustomerCreatesWhenTheLookupMisses()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.NotFound, "{}")
            .Enqueue(HttpStatusCode.Created, """{"customer":{"id":99268557,"reference":"eshoponweb-user:u1","first_name":"Sam","last_name":"User","email":"u1@example.com"}}""");
        var (client, _) = CreateClient(handler);

        var customer = await client.EnsureCustomerAsync("eshoponweb-user:u1", "Sam", "User", "u1@example.com");

        Assert.Equal(99268557, customer.CustomerId);
        Assert.Equal("eshoponweb-user:u1", customer.Reference);
    }

    [Fact]
    public async Task EnsureCustomerAdoptsExistingCustomerWhenCreateReportsDuplicateReference()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.NotFound, "{}")
            .Enqueue((HttpStatusCode)422, """{"errors":["Reference: must be unique - that value has been taken."]}""")
            .Enqueue(HttpStatusCode.OK, """{"customer":{"id":99268557,"reference":"eshoponweb-user:u1","first_name":"Sam","last_name":"User","email":"u1@example.com"}}""");
        var (client, _) = CreateClient(handler);

        var customer = await client.EnsureCustomerAsync("eshoponweb-user:u1", "Sam", "User", "u1@example.com");

        Assert.Equal(99268557, customer.CustomerId);
    }

    [Fact]
    public async Task SubscribeReturnsNullWhenReferenceConflictIndicatesADuplicateRequest()
    {
        var handler = new StubHandler()
            .Enqueue((HttpStatusCode)422, """{"errors":["Reference: must be unique - that value has been taken."]}""");
        var (client, _) = CreateClient(handler);

        var result = await client.SubscribeAsync(555, "eshop-pro", "eshoponweb-sub:u1:eshop-pro");

        Assert.Null(result);
    }

    [Fact]
    public async Task SubscribeParsesActiveSubscriptionWithNextBillingDate()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.Created, """
            {"subscription":{"id":94685018,"state":"active","reference":"eshoponweb-sub:u1:eshop-pro","customer_id":555,
              "product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"currency":"USD",
              "current_period_ends_at":"2026-11-06T19:43:51+05:00","next_assessment_at":"2026-11-06T19:43:51+05:00",
              "activated_at":"2026-10-06T19:43:52+05:00","created_at":"2026-10-06T19:43:51+05:00","canceled_at":null,
              "payment_collection_method":"remittance"}}
            """);
        var (client, _) = CreateClient(handler);

        var subscription = await client.SubscribeAsync(555, "eshop-pro", "eshoponweb-sub:u1:eshop-pro");

        Assert.NotNull(subscription);
        Assert.Equal("active", subscription!.State);
        Assert.True(subscription.IsLive);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.NotNull(subscription.NextBillingAt);
    }

    [Fact]
    public async Task SubscribeSurfacesProviderValidationErrors()
    {
        var handler = new StubHandler().Enqueue((HttpStatusCode)422, """{"errors":["No payment method was on file for the $299.00 balance"]}""");
        var (client, _) = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.SubscribeAsync(555, "eshop-pro", "eshoponweb-sub:u1:eshop-pro"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method", string.Join(";", ex.ProviderErrors));
    }

    [Fact]
    public async Task SubdomainDerivesTheUSChargifyBaseUrl()
    {
        Assert.Equal("https://my-site.chargify.com", Settings.ResolveBaseUrl());
    }

    [Fact]
    public async Task BaseUrlOverrideIsUsedVerbatim()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "ignored", BaseUrl = "https://eu.ebilling.example/", ProductFamilyHandle = "f" };
        Assert.Equal("https://eu.ebilling.example", settings.ResolveBaseUrl());
        await Task.CompletedTask;
    }
}
