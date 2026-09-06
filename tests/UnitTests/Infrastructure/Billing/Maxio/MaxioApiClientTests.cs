using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Pins the wire contract against payloads captured from a real Maxio sandbox, so a change to the
/// request shape or to the response mapping fails here rather than in production.
/// </summary>
public class MaxioApiClientTests
{
    [Fact]
    public async Task AuthenticatesWithTheApiKeyAsBasicUserAndLiteralXPassword()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));
        var client = CreateClient(handler, s => s.ApiKey = "abc123");

        await client.GetSiteAsync();

        var header = handler.Requests.Single().Headers.Authorization;
        Assert.Equal("Basic", header!.Scheme);
        Assert.Equal("abc123:x", Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!)));
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandleBecauseNumericIdsAreNotStable()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var client = CreateClient(handler);

        await client.ListProductsForFamilyAsync("demo-family");

        Assert.Contains("product_families/handle%3Ademo-family/products.json",
            handler.Requests.Single().RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnwrapsProductEnvelopesAndFollowsPaginationToTheEnd()
    {
        var page = string.Join(",", Enumerable.Range(0, 200).Select(i =>
            $$$"""{"product":{"id":{{{i}}},"handle":"plan-{{{i}}}","name":"Plan {{{i}}}","price_in_cents":100}}"""));

        var responses = new Queue<string>(new[] { $"[{page}]", """[{"product":{"id":999,"handle":"last","name":"Last","price_in_cents":1}}]""" });
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, responses.Dequeue()));
        var client = CreateClient(handler);

        var products = await client.ListProductsForFamilyAsync("demo-family");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsACustomerLookupAsNullWhenMaxioAnswers404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        var client = CreateClient(handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("eshoponweb:nobody@example.com"));
    }

    [Fact]
    public async Task EscapesTheReferenceItLooksUp()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        var client = CreateClient(handler);

        await client.FindCustomerByReferenceAsync("eshoponweb:demouser@microsoft.com");

        Assert.Contains("reference=eshoponweb%3Ademouser%40microsoft.com",
            handler.Requests.Single().RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurfacesMaxioValidationErrorsAndFlagsATakenReference()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateCustomerAsync(new MaxioCreateCustomerRequest()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsReferenceTaken);
        Assert.Contains("must be unique", exception.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotTreatAnUnrelatedValidationErrorAsATakenReference()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.False(exception.IsReferenceTaken);
    }

    [Fact]
    public async Task SendsTheReferenceInsideTheSubscriptionObject()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"subscription":{"id":1,"state":"active"}}"""));
        var client = CreateClient(handler);

        await client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = "demo-pro",
                CustomerId = 42,
                PaymentCollectionMethod = "remittance",
                Reference = "eshoponweb:demouser@microsoft.com|demo-pro|0"
            }
        });

        var body = handler.Bodies.Single();
        Assert.Contains("""{"subscription":{"product_handle":"demo-pro","customer_id":42,"payment_collection_method":"remittance","reference":"eshoponweb:demouser@microsoft.com|demo-pro|0"}}""",
            body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetriesATransientFailureAndThenSucceeds()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") }
                : Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}""");
        });

        var client = CreateClient(handler);

        var site = await client.GetSiteAsync();

        Assert.Equal("USD", site.Currency);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfAttempts()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("") });
        var client = CreateClient(handler, s => s.MaxRetries = 1);

        await Assert.ThrowsAsync<MaxioApiException>(() => client.GetSiteAsync());

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryAClientError()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<MaxioApiException>(() => client.CreateCustomerAsync(new MaxioCreateCustomerRequest()));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MapsTheSubscriptionFieldsTheConfirmationDependsOn()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """
            {"subscription":{"id":94208179,"state":"active","reference":"eshoponweb:demouser@microsoft.com|demo-pro|0",
             "balance_in_cents":29900,"product_price_in_cents":29900,"currency":"USD",
             "current_period_started_at":"2026-09-06T09:36:49+05:00","current_period_ends_at":"2026-10-06T09:36:49+05:00",
             "next_assessment_at":"2026-10-06T09:36:49+05:00","activated_at":"2026-09-06T09:36:50+05:00",
             "canceled_at":null,"payment_collection_method":"remittance",
             "product":{"id":7130997,"name":"Pro Plan","handle":"demo-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"},
             "customer":{"id":98837075,"reference":"eshoponweb:demouser@microsoft.com","email":"demouser@microsoft.com"}}}
            """));

        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest());

        Assert.Equal(94208179, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal("demo-pro", subscription.Product!.Handle);
        Assert.Equal("month", subscription.Product.IntervalUnit);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 9, 36, 49, TimeSpan.FromHours(5)), subscription.NextAssessmentAt);
        Assert.Equal(98837075, subscription.Customer!.Id);
    }

    [Fact]
    public async Task UsesTheConfiguredBaseUrlVerbatimWhenOneIsSupplied()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));
        var client = CreateClient(handler, s => s.BaseUrl = "https://maxio.internal.example/gateway");

        await client.GetSiteAsync();

        Assert.Equal("https://maxio.internal.example/gateway/site.json", handler.Requests.Single().RequestUri!.ToString());
    }

    private static MaxioApiClient CreateClient(StubHandler handler, Action<MaxioSettings>? configure = null)
    {
        var settings = MaxioTestData.Settings(configure);
        var httpClient = new HttpClient(handler) { BaseAddress = settings.Value.ResolveBaseAddress() };
        return new MaxioApiClient(httpClient, settings, NullLogger<MaxioApiClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string payload) =>
        new(statusCode) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _respond(request);
        }
    }
}
