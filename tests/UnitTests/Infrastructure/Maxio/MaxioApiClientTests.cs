using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Verifies that the client speaks exactly what the Maxio OpenAPI specification describes: paths,
/// query parameters, the Basic auth scheme, the response envelopes and the error envelopes.
/// </summary>
public class MaxioApiClientTests
{
    private const string BaseUrl = "https://stub.local";

    private static (MaxioApiClient Client, StubHandler Handler) CreateClient(params StubResponse[] responses)
    {
        var handler = new StubHandler(responses);
        var httpClient = new HttpClient(handler);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            ProductFamilyHandle = "demo-subscriptions",
            BaseUrl = BaseUrl,
            MaxRetryAttempts = 0
        });

        return (new MaxioApiClient(httpClient, settings, NullLogger<MaxioApiClient>.Instance), handler);
    }

    [Fact]
    public async Task SendsTheApiKeyAsHttpBasicWithTheLiteralPasswordX()
    {
        var (client, handler) = CreateClient(StubResponse.Json(HttpStatusCode.OK, "[]"));

        await client.ListProductsForProductFamilyAsync("handle:demo-subscriptions");

        var authorization = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal("test-key:x", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task ListsProductsForAFamilyAddressedByHandle()
    {
        var (client, handler) = CreateClient(StubResponse.Json(HttpStatusCode.OK, """
            [{"product":{"id":7130993,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month","require_credit_card":false,
              "product_family":{"id":1,"handle":"demo-subscriptions"}}}]
            """));

        var products = await client.ListProductsForProductFamilyAsync("handle:demo-subscriptions");

        var uri = handler.Requests[0].RequestUri!;
        Assert.Equal("/product_families/handle%3Ademo-subscriptions/products.json", uri.AbsolutePath);
        Assert.Contains("per_page=200", uri.Query);
        Assert.Contains("page=1", uri.Query);
        Assert.Contains("include_archived=false", uri.Query);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.Equal("demo-subscriptions", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task StopsPagingWhenAPageIsNotFull()
    {
        var (client, handler) = CreateClient(StubResponse.Json(HttpStatusCode.OK, """[{"product":{"handle":"a"}}]"""));

        await client.ListProductsForProductFamilyAsync("handle:demo-subscriptions");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TreatsAMissingCustomerAsNoResultRatherThanAnError()
    {
        var (client, handler) = CreateClient(StubResponse.Json(HttpStatusCode.NotFound, string.Empty));

        var customer = await client.ReadCustomerByReferenceAsync("eshoponweb:demouser@microsoft.com");

        Assert.Null(customer);
        Assert.Equal("/customers/lookup.json", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("reference=eshoponweb%3Ademouser%40microsoft.com", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task TreatsAMissingSubscriptionReferenceAsNoResult()
    {
        var (client, handler) = CreateClient(StubResponse.Json(HttpStatusCode.NotFound, string.Empty));

        var subscription = await client.FindSubscriptionByReferenceAsync("eshoponweb:demouser:eshop-pro");

        Assert.Null(subscription);
        Assert.Equal("/subscriptions/lookup.json", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PostsTheSubscriptionEnvelopeTheSpecDefines()
    {
        var (client, handler) = CreateClient(StubResponse.Json(HttpStatusCode.Created, """
            {"subscription":{"id":94209538,"state":"active","reference":"eshoponweb:demouser:eshop-pro",
             "product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-10-06T14:55:18+05:00",
             "product":{"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"},
             "customer":{"id":98838137,"email":"demouser@microsoft.com"}}}
            """));

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 98838137,
                Reference = "eshoponweb:demouser:eshop-pro",
                PaymentCollectionMethod = "remittance"
            }
        });

        Assert.Equal("/subscriptions.json", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":98838137", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        // Optional properties that were not set must not be sent at all.
        Assert.DoesNotContain("customer_reference", body);

        Assert.Equal(94209538, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
    }

    [Fact]
    public async Task SurfacesTheErrorListEnvelopeOnRejection()
    {
        var (client, _) = CreateClient(StubResponse.Json(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.True(exception.IsValidationFailure);
        Assert.Equal("No payment method was on file for the $299.00 balance", exception.Errors.Single());
    }

    [Fact]
    public async Task SurfacesTheKeyedCustomerErrorEnvelopeOnRejection()
    {
        var (client, _) = CreateClient(StubResponse.Json(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":{"customer":"can't be blank"}}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomerRequest()));

        Assert.Equal("customer: can't be blank", exception.Errors.Single());
    }

    [Fact]
    public async Task ReportsCredentialRejectionsDistinctly()
    {
        var (client, _) = CreateClient(StubResponse.Json(HttpStatusCode.Unauthorized, string.Empty));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => client.ReadSiteAsync());

        Assert.True(exception.IsAuthenticationFailure);
        Assert.DoesNotContain("test-key", exception.Message);
    }

    [Fact]
    public async Task ReportsAnUnreachableApiAsATransportFailure()
    {
        var handler = new StubHandler(Array.Empty<StubResponse>())
        {
            ThrowOnSend = new HttpRequestException("no route to host")
        };

        var client = new MaxioApiClient(
            new HttpClient(handler),
            Options.Create(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = BaseUrl }),
            NullLogger<MaxioApiClient>.Instance);

        await Assert.ThrowsAsync<MaxioTransportException>(() => client.ReadSiteAsync());
    }

    private sealed class StubResponse
    {
        public HttpStatusCode StatusCode { get; init; }

        public string Body { get; init; } = string.Empty;

        public static StubResponse Json(HttpStatusCode statusCode, string body) => new()
        {
            StatusCode = statusCode,
            Body = body
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<StubResponse> _responses;

        public StubHandler(IEnumerable<StubResponse> responses) => _responses = new Queue<StubResponse>(responses);

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> RequestBodies { get; } = new();

        public Exception? ThrowOnSend { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var stub = _responses.Count > 0 ? _responses.Dequeue() : StubResponse.Json(HttpStatusCode.OK, "[]");

            return new HttpResponseMessage(stub.StatusCode)
            {
                Content = new StringContent(stub.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
