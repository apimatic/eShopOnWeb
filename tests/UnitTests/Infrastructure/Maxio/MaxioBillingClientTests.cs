using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingClientTests : IDisposable
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MaxioBillingClient _sut;

    public MaxioBillingClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://cp-exp-4.chargify.com/") };
        _sut = new MaxioBillingClient(_httpClient, Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    public void Dispose() => _httpClient.Dispose();

    [Fact]
    public async Task ListPlans_reads_wrapped_products_and_skips_archived()
    {
        _handler.EnqueueJson("""
            [
              {"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_family":{"id":1,"name":"eShop","handle":"eshop-subscribe"}}},
              {"product":{"id":999,"name":"Old","handle":"old-plan","price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":true,"archived_at":"2024-01-01T00:00:00Z"}}
            ]
            """);

        var plans = await _sut.ListPlansAsync("eshop-subscribe");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json?per_page=200", request.RequestUri!.PathAndQuery);

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(29900, plans[0].PriceInCents);
        Assert.False(plans[0].RequiresPaymentMethod);
        Assert.Equal("eshop-subscribe", plans[0].ProductFamilyHandle);
    }

    [Fact]
    public async Task FindCustomerByReference_returns_null_on_404()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, "{}");

        var customer = await _sut.FindCustomerByReferenceAsync("eshoponweb:abc");

        Assert.Null(customer);
    }

    [Fact]
    public async Task FindCustomerByReference_reads_lookup_response()
    {
        _handler.EnqueueJson("""{"customer":{"id":42,"reference":"eshoponweb:abc","email":"a@b.c"}}""");

        var customer = await _sut.FindCustomerByReferenceAsync("eshoponweb:abc");

        Assert.NotNull(customer);
        Assert.Equal(42, customer!.Id);
        Assert.Equal("eshoponweb:abc", customer.Reference);
        Assert.Contains("reference=eshoponweb%3Aabc", _handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task CreateCustomer_posts_customer_envelope()
    {
        _handler.EnqueueJson("""{"customer":{"id":42,"reference":"eshoponweb:abc","first_name":"Demo","last_name":"User","email":"a@b.c"}}""");

        var customer = await _sut.CreateCustomerAsync(new SubscriberProfile
        {
            Reference = "eshoponweb:abc",
            FirstName = "Demo",
            LastName = "User",
            Email = "a@b.c",
        });

        Assert.Equal(42, customer.Id);
        var request = _handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/customers.json", request.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(_handler.Bodies.Single()!);
        var customerNode = body.RootElement.GetProperty("customer");
        Assert.Equal("eshoponweb:abc", customerNode.GetProperty("reference").GetString());
        Assert.Equal("a@b.c", customerNode.GetProperty("email").GetString());
    }

    [Fact]
    public async Task CreateSubscription_sends_uniqueness_token_at_top_level()
    {
        _handler.EnqueueJson("""
            {"subscription":{"id":8001,"customer_id":42,"state":"active","product_price_in_cents":29900,"created_at":"2026-09-08T12:00:00Z","current_period_ends_at":"2026-10-08T12:00:00Z","product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
            """);

        var subscription = await _sut.CreateSubscriptionAsync(42, "eshop-pro", "ref-1", "token-123");

        var request = _handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions.json", request.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(_handler.Bodies.Single()!);
        Assert.Equal("token-123", body.RootElement.GetProperty("uniqueness_token").GetString());
        var sub = body.RootElement.GetProperty("subscription");
        Assert.Equal(42, sub.GetProperty("customer_id").GetInt64());
        Assert.Equal("eshop-pro", sub.GetProperty("product_handle").GetString());
        Assert.Equal("ref-1", sub.GetProperty("reference").GetString());

        Assert.Equal(8001, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), subscription.CurrentPeriodEndsAt);
    }

    [Fact]
    public async Task Failed_response_surfaces_status_and_parsed_errors()
    {
        _handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["Product handle is invalid","Payment method required"]}""");

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => _sut.CreateSubscriptionAsync(1, "nope", null, "token"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Equal(2, ex.Errors.Count);
        Assert.False(ex.IsDuplicateSubmission);
    }

    [Fact]
    public async Task Duplicate_prevention_response_is_flagged()
    {
        _handler.Enqueue(HttpStatusCode.Conflict, """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""");

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => _sut.CreateSubscriptionAsync(1, "plan", null, "token"));

        Assert.True(ex.IsDuplicateSubmission);
    }

    [Fact]
    public async Task Get_retries_transient_failures_before_succeeding()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests, "{}");
        _handler.EnqueueJson("""[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var plans = await _sut.ListPlansAsync("eshop-subscribe");

        Assert.Single(plans);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task ListCustomerSubscriptions_reads_wrapped_array()
    {
        _handler.EnqueueJson("""
            [
              {"subscription":{"id":10,"state":"active","product_price_in_cents":29900,"created_at":"2026-09-01T00:00:00Z","current_period_ends_at":"2026-10-01T00:00:00Z","product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
            ]
            """);

        var subscriptions = await _sut.ListCustomerSubscriptionsAsync(42);

        var sub = Assert.Single(subscriptions);
        Assert.Equal(10, sub.Id);
        Assert.Equal(42, sub.CustomerId);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("/customers/42/subscriptions.json", _handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();

        public void EnqueueJson(string json) => Enqueue(HttpStatusCode.OK, json);

        public void Enqueue(HttpStatusCode statusCode, string json)
        {
            _responses.Enqueue(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture (and thereby consume) the content before the client may dispose it.
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(request);
            var factory = _responses.Count > 0 ? _responses.Dequeue() : () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            return factory();
        }
    }
}
