using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioApiClientTests
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private MaxioApiClient CreateClient()
    {
        var monitor = new StaticOptionsMonitor<MaxioOptions>(_options);

        // The same handler pipeline the application composes: authentication sits closest to the wire.
        var authentication = new MaxioAuthenticationHandler(monitor) { InnerHandler = _handler };

        return new MaxioApiClient(new HttpClient(authentication), monitor,
            NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task ListsProductsOfAFamilyFromTheSpecifiedPathAndUnwrapsTheEnvelope()
    {
        _handler.Respond(HttpStatusCode.OK, """
            [
              { "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                             "interval": 1, "interval_unit": "month", "require_credit_card": false,
                             "archived_at": null,
                             "product_family": { "id": 9, "handle": "eshop-subscribe" } } }
            ]
            """);

        var products = await CreateClient().ListProductsForProductFamilyAsync("handle:eshop-subscribe", 1, 200);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://acme.chargify.com/product_families/handle:eshop-subscribe/products.json",
            request.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("?page=1&per_page=200", request.RequestUri.Query);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task SendsTheApiKeyAsBasicAuthenticationWithThePasswordX()
    {
        _handler.Respond(HttpStatusCode.OK, "[]");

        await CreateClient().ListProductsForProductFamilyAsync("handle:eshop-subscribe", 1, 200);

        var authorization = Assert.Single(_handler.Requests).Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal("test-key:x",
            Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task LooksACustomerUpByReference()
    {
        _handler.Respond(HttpStatusCode.OK,
            """{ "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "eshoponweb:demouser@microsoft.com" } }""");

        var customer = await CreateClient().ReadCustomerByReferenceAsync("eshoponweb:demouser@microsoft.com");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("/customers/lookup.json", request.RequestUri!.AbsolutePath);
        Assert.Equal("?reference=eshoponweb%3Ademouser%40microsoft.com", request.RequestUri.Query);
        Assert.Equal(42, customer!.Id);
    }

    [Fact]
    public async Task TreatsAnUnknownCustomerReferenceAsNoCustomer()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().ReadCustomerByReferenceAsync("eshoponweb:nobody@example.com"));
    }

    [Fact]
    public async Task PostsASubscriptionInTheShapeTheSpecificationDefines()
    {
        _handler.Respond(HttpStatusCode.Created,
            """{ "subscription": { "id": 7, "state": "active", "product": { "handle": "eshop-pro" } } }""");

        var subscription = await CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "eshoponweb:demouser@microsoft.com:eshop-pro"
            }
        });

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://acme.chargify.com/subscriptions.json", request.RequestUri!.ToString());

        var body = Assert.Single(_handler.RequestBodies);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":42", body);
        // Properties that were not set are left out rather than sent as nulls.
        Assert.DoesNotContain("customer_reference", body);

        Assert.Equal(7, subscription.Id);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public async Task SurfacesARejectedPayloadAsAValidationProblem()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity,
            """{ "errors": ["Email address: cannot be blank."] }""");

        var exception = await Assert.ThrowsAsync<MaxioValidationException>(
            () => CreateClient().CreateCustomerAsync(new CreateCustomerRequest()));

        Assert.IsAssignableFrom<BillingValidationException>(exception);
        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Email address: cannot be blank.", exception.Errors);
        Assert.Contains("Email address: cannot be blank.", exception.Message);
    }

    [Fact]
    public async Task SurfacesAnUpstreamFailureAsAProviderProblem()
    {
        _handler.Respond(HttpStatusCode.Forbidden, """{ "error": "Access denied" }""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient().ReadSiteAsync());

        Assert.Equal(403, exception.StatusCode);
        Assert.Contains("Access denied", exception.Message);
    }

    [Fact]
    public async Task RefusesToCallMaxioWithoutConfiguration()
    {
        _options.ApiKey = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateClient().ReadSiteAsync());
        Assert.Empty(_handler.Requests);
    }
}
