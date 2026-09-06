using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

/// <summary>
/// Asserts that the hand-written client speaks exactly the protocol the Maxio OpenAPI specification
/// describes: paths, query parameters, request envelopes, response envelopes and error models.
/// </summary>
public class MaxioApiClientTests
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ListsProductsForAProductFamilyAddressedByHandle()
    {
        const string json = """
        [
          {
            "product": {
              "id": 7130993,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "archived_at": null,
              "product_price_point_id": 4626114,
              "product_price_point_name": "Original",
              "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" }
            }
          }
        ]
        """;
        _handler.Respond(HttpStatusCode.OK, json);

        var products = await CreateClient().ListProductsForProductFamilyAsync("handle:eshop-subscribe");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json", request.Uri.AbsolutePath);
        Assert.Equal("?include_archived=false", request.Uri.Query);

        var product = Assert.Single(products).Product;
        Assert.NotNull(product);
        Assert.Equal("eshop-pro", product!.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", product.ProductFamily?.Handle);
    }

    [Fact]
    public async Task SendsTheApiKeyAsTheBasicAuthUsernameWithPasswordX()
    {
        _handler.Respond(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}""");

        await CreateClient().ReadSiteAsync();

        var request = Assert.Single(_handler.Requests);
        Assert.NotNull(request.Authorization);
        var credential = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Authorization!.Split(' ')[1]));
        Assert.Equal("test-api-key:x", credential);
    }

    [Fact]
    public async Task ReadsACustomerByReferenceAndEscapesIt()
    {
        _handler.Respond(HttpStatusCode.OK, """{"customer":{"id":9,"reference":"eshop:demouser@microsoft.com","email":"demouser@microsoft.com"}}""");

        var response = await CreateClient().ReadCustomerByReferenceAsync("eshop:demouser@microsoft.com");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("/customers/lookup.json", request.Uri.AbsolutePath);
        Assert.Equal("?reference=eshop%3Ademouser%40microsoft.com", request.Uri.Query);
        Assert.Equal(9, response?.Customer?.Id);
    }

    [Fact]
    public async Task TreatsCustomerLookupNotFoundAsNoCustomer()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().ReadCustomerByReferenceAsync("eshop:nobody@example.com"));
    }

    [Fact]
    public async Task TreatsSubscriptionLookupNotFoundAsNoSubscription()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().FindSubscriptionAsync("eshop:sub:nobody@example.com:eshop-pro"));
    }

    [Fact]
    public async Task PostsTheCreateSubscriptionEnvelopeTheSpecDefines()
    {
        _handler.Respond(HttpStatusCode.Created, """{"subscription":{"id":94213499,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-10-06T23:56:50+05:00","currency":"USD","product":{"id":1,"handle":"eshop-pro","name":"Pro Plan"}}}""");

        var response = await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "eshop:sub:demouser@microsoft.com:eshop-pro",
                PaymentCollectionMethod = "remittance"
            }
        });

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions.json", request.Uri.AbsolutePath);

        using var body = JsonDocument.Parse(request.Body!);
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(42, subscription.GetProperty("customer_id").GetInt32());
        Assert.Equal("eshop:sub:demouser@microsoft.com:eshop-pro", subscription.GetProperty("reference").GetString());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());

        Assert.Equal("active", response.Subscription?.State);
        Assert.Equal(29900, response.Subscription?.ProductPriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 23, 56, 50, TimeSpan.FromHours(5)), response.Subscription?.NextAssessmentAt);
    }

    [Fact]
    public async Task OmitsMembersThatWereNotSet()
    {
        _handler.Respond(HttpStatusCode.Created, """{"subscription":{"id":1,"state":"active"}}""");

        await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription { ProductHandle = "eshop-pro" }
        });

        using var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body!);
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.False(subscription.TryGetProperty("customer_id", out _));
        Assert.False(subscription.TryGetProperty("reference", out _));
    }

    [Fact]
    public async Task SurfacesTheProviderErrorListFromA422()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription { ProductHandle = "eshop-pro" }
        }));

        Assert.Equal("createSubscription", exception.OperationId);
        Assert.Equal(422, exception.StatusCode);
        Assert.True(exception.IsRequestRejected);
        Assert.Equal(new[] { "No payment method was on file for the $299.00 balance" }, exception.Errors);
        Assert.Contains("No payment method was on file", exception.Message);
    }

    [Fact]
    public async Task SurfacesTheKeyedCustomerErrorShape()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"can't be blank"}}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer { FirstName = "A", LastName = "B", Email = "a@b.test" }
        }));

        Assert.Equal(new[] { "customer: can't be blank" }, exception.Errors);
    }

    [Fact]
    public async Task ReportsRejectedCredentialsAsAConfigurationProblem()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateClient().ReadSiteAsync());

        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public async Task DoesNotTreatAServerErrorAsACallerError()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, "boom");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().ReadSiteAsync());

        Assert.False(exception.IsRequestRejected);
    }

    private MaxioApiClient CreateClient()
    {
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://acme.chargify.com/")
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test-api-key:x")));

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }
}
