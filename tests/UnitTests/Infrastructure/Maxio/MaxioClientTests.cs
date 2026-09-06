using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioClientTests
{
    private const string ApiKey = "test-api-key";

    private static (MaxioClient Client, StubHttpMessageHandler Handler) CreateClient(
        StubHttpMessageHandler handler, Action<MaxioOptions>? configure = null)
    {
        var options = new MaxioOptions
        {
            ApiKey = ApiKey,
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            MaxRetryAttempts = 0
        };

        configure?.Invoke(options);

        var client = new MaxioClient(
            new HttpClient(handler),
            Options.Create(options),
            NullLogger<MaxioClient>.Instance);

        return (client, handler);
    }

    [Fact]
    public void ThrowsWhenConfigurationIsIncomplete()
    {
        var options = Options.Create(new MaxioOptions { Subdomain = "acme" });

        var exception = Assert.Throws<BillingConfigurationException>(() =>
            new MaxioClient(new HttpClient(new StubHttpMessageHandler()), options, NullLogger<MaxioClient>.Instance));

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task SendsBasicAuthWithApiKeyAsUserAndXAsPassword()
    {
        var (client, handler) = CreateClient(new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));

        await client.ReadSiteAsync();

        var authorization = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal($"{ApiKey}:x", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task ReadSiteTargetsTheEnvironmentServerFromTheSpecification()
    {
        var (client, handler) = CreateClient(new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));

        var site = await client.ReadSiteAsync();

        Assert.Equal("https://acme.chargify.com/site.json", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("USD", site!.Currency);
    }

    [Fact]
    public async Task ExplicitBaseUrlIsUsedVerbatim()
    {
        var (client, handler) = CreateClient(
            new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """{"site":{"id":1}}"""),
            o => o.BaseUrl = "https://localhost:9443/maxio");

        await client.ReadSiteAsync();

        Assert.Equal("https://localhost:9443/maxio/site.json", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ListProductsForProductFamilyUsesTheHandlePrefixAndPaging()
    {
        var (client, handler) = CreateClient(new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
            [{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month","product_family":{"id":9,"handle":"eshop-subscribe"}}}]
            """));

        var products = await client.ListProductsForProductFamilyAsync("handle:eshop-subscribe", page: 2, perPage: 200);

        Assert.Equal(
            "https://acme.chargify.com/product_families/handle:eshop-subscribe/products.json?page=2&per_page=200",
            Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString()));
        Assert.Equal("eshop-pro", Assert.Single(products).Handle);
    }

    [Fact]
    public async Task ListProductsSurfacesNotFoundBecauseAnUnknownFamilyIsAFault()
    {
        var (client, _) = CreateClient(new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound, "\"A valid product_family_id is required\""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.ListProductsForProductFamilyAsync("handle:nope", 1, 200));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task LookupsReturnNullOnNotFound()
    {
        var (client, _) = CreateClient(new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.NotFound)
            .Enqueue(HttpStatusCode.NotFound)
            .Enqueue(HttpStatusCode.NotFound));

        Assert.Null(await client.ReadCustomerByReferenceAsync("missing"));
        Assert.Null(await client.FindSubscriptionAsync("missing"));
        Assert.Null(await client.ReadProductByHandleAsync("missing"));
    }

    [Fact]
    public async Task LookupsEscapeTheReferenceQueryValue()
    {
        var (client, handler) = CreateClient(new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound));

        await client.ReadCustomerByReferenceAsync("a b&c=d");

        Assert.Equal(
            "https://acme.chargify.com/customers/lookup.json?reference=a%20b%26c%3Dd",
            handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateCustomerPostsTheSpecifiedEnvelope()
    {
        var (client, handler) = CreateClient(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Created, """
            {"customer":{"id":42,"reference":"eshoponweb-demo","email":"demo@example.com"}}
            """));

        var customer = await client.CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = "Demo",
                LastName = "User",
                Email = "demo@example.com",
                Reference = "eshoponweb-demo"
            }
        });

        Assert.Equal("https://acme.chargify.com/customers.json", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("\"first_name\":\"Demo\"", handler.RequestBodies[0]);
        Assert.Contains("\"reference\":\"eshoponweb-demo\"", handler.RequestBodies[0]);
        Assert.DoesNotContain("\"organization\"", handler.RequestBodies[0]); // nulls are omitted
        Assert.Equal(42, customer.Id);
    }

    [Fact]
    public async Task CreateSubscriptionPostsTheSpecifiedEnvelopeAndReadsTheResponse()
    {
        var (client, handler) = CreateClient(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Created, """
            {"subscription":{"id":7,"state":"active","reference":"eshoponweb-demo-eshop-pro",
              "product_price_in_cents":29900,"currency":"USD",
              "current_period_ends_at":"2026-10-06T20:10:59+05:00",
              "next_assessment_at":"2026-10-06T20:10:59+05:00",
              "customer":{"id":42,"reference":"eshoponweb-demo"},
              "product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"}}}
            """));

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "eshoponweb-demo-eshop-pro",
                PaymentCollectionMethod = MaxioCollectionMethods.Remittance
            }
        });

        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.RequestBodies[0]);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", handler.RequestBodies[0]);
        Assert.Equal(7, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 20, 10, 59, TimeSpan.FromHours(5)), subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task UnprocessableEntityBecomesAMaxioApiExceptionCarryingTheErrorList()
    {
        var (client, _) = CreateClient(new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal("createSubscription", exception.OperationId);
        Assert.Equal("Reference: must be unique - that value has been taken.", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task CustomerErrorMapShapeIsFlattened()
    {
        var (client, _) = CreateClient(new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":{"customer":"can't be blank"}}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateCustomerAsync(new MaxioCreateCustomerRequest()));

        Assert.Equal("customer: can't be blank", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task SingleErrorShapeIsFlattened()
    {
        var (client, _) = CreateClient(new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.BadRequest, """{"error":"Something went wrong"}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateCustomerAsync(new MaxioCreateCustomerRequest()));

        Assert.Equal("Something went wrong", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task UnauthorizedIsFlaggedAsAnAuthenticationFailure()
    {
        var (client, _) = CreateClient(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.ListCustomerSubscriptionsAsync(42));

        Assert.True(exception.IsAuthenticationFailure);
    }

    [Fact]
    public async Task UnreachableApiBecomesATransportException()
    {
        var (client, _) = CreateClient(new ThrowingHandler(new HttpRequestException("no route to host")));

        await Assert.ThrowsAsync<MaxioTransportException>(() => client.ReadSiteAsync());
    }

    private sealed class ThrowingHandler : StubHttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }
}
