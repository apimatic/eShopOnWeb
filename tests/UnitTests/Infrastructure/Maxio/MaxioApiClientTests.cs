using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Verifies the transport against the shapes declared in maxio-spec/openapi.yaml: request paths,
/// query parameters, envelopes and error handling.
/// </summary>
public class MaxioApiClientTests
{
    private readonly StubHttpMessageHandler _handler = new();

    private MaxioApiClient CreateClient()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("key:x")));

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task ListsProductsForProductFamilyByHandle()
    {
        _handler.Respond(HttpStatusCode.OK, """
            [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,
              "interval_unit":"month","require_credit_card":false,"taxable":false,
              "product_family":{"id":9,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}]
            """);

        var products = await CreateClient().ListProductsForProductFamilyAsync("handle:eshop-subscribe", page: 1, perPage: 200, includeArchived: false);

        var request = _handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json", request.RequestUri!.AbsolutePath);
        Assert.Contains("page=1", request.RequestUri.Query);
        Assert.Contains("per_page=200", request.RequestUri.Query);
        Assert.Contains("include_archived=false", request.RequestUri.Query);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task ReadsCustomerByReferenceAndUrlEncodesIt()
    {
        _handler.Respond(HttpStatusCode.OK, """{"customer":{"id":42,"email":"a@b.com","reference":"eshoponweb:a@b.com"}}""");

        var customer = await CreateClient().ReadCustomerByReferenceAsync("eshoponweb:a@b.com");

        Assert.Equal("/customers/lookup.json", _handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("reference=eshoponweb%3Aa%40b.com", _handler.Requests[0].RequestUri!.Query);
        Assert.Equal(42, customer!.Id);
    }

    [Fact]
    public async Task ReturnsNullWhenCustomerLookupIsNotFound()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().ReadCustomerByReferenceAsync("missing"));
    }

    [Fact]
    public async Task ReturnsNullWhenSubscriptionLookupIsNotFound()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().FindSubscriptionByReferenceAsync("missing"));
    }

    [Fact]
    public async Task SendsCreateSubscriptionEnvelopeWithoutNullMembers()
    {
        _handler.Respond(HttpStatusCode.Created, """{"subscription":{"id":7,"state":"active"}}""");

        var subscription = await CreateClient().CreateSubscriptionAsync(new CreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 42,
            PaymentCollectionMethod = "remittance"
        });

        Assert.Equal(7, subscription.Id);
        Assert.Equal("/subscriptions.json", _handler.Requests[0].RequestUri!.AbsolutePath);

        var body = _handler.RequestBodies[0]!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":42", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.DoesNotContain("reference", body);
        Assert.DoesNotContain("product_price_point_handle", body);
    }

    [Fact]
    public async Task SendsBasicAuthorizationHeader()
    {
        _handler.Respond(HttpStatusCode.OK, """{"site":{"id":1,"relationship_invoicing_enabled":true}}""");

        await CreateClient().ReadSiteAsync();

        var authorization = _handler.Requests[0].Headers.Authorization;
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal("key:x", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task ThrowsWithParsedErrorsOnUnprocessableEntity()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":["Product: is not valid."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient().CreateSubscriptionAsync(new CreateSubscription { ProductHandle = "nope" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsValidationFailure);
        Assert.Equal("Product: is not valid.", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task UnwrapsCustomerSubscriptionEnvelopes()
    {
        _handler.Respond(HttpStatusCode.OK, """
            [{"subscription":{"id":1,"state":"active","product":{"handle":"eshop-pro"}}},
             {"subscription":{"id":2,"state":"canceled","product":{"handle":"basic-plan"}}}]
            """);

        var subscriptions = await CreateClient().ListCustomerSubscriptionsAsync(42);

        Assert.Equal("/customers/42/subscriptions.json", _handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("active", subscriptions[0].State);
        Assert.Equal("basic-plan", subscriptions[1].Product!.Handle);
    }
}
