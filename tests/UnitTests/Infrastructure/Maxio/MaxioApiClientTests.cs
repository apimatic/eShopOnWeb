using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientTests
{
    private readonly StubHttpMessageHandler _handler = new();

    private MaxioApiClient CreateClient() => new(
        new HttpClient(_handler) { BaseAddress = new Uri("https://acme.chargify.com/") },
        NullLogger<MaxioApiClient>.Instance);

    private Uri LastUri => _handler.Requests[^1].RequestUri!;

    [Fact]
    public async Task ReadsTheSiteCurrency()
    {
        _handler.Respond(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD","subdomain":"acme"}}""");

        var site = await CreateClient().GetSiteAsync();

        Assert.Equal("USD", site.Currency);
        Assert.Equal("https://acme.chargify.com/site.json", LastUri.ToString());
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandleRatherThanId()
    {
        _handler.Respond(HttpStatusCode.OK, """
            [{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month","require_credit_card":false,
              "product_family":{"id":9,"handle":"eshop-subscribe"}}}]
            """);

        var products = await CreateClient().ListProductsForFamilyAsync("eshop-subscribe");

        // Numeric ids are reassigned when the catalog is re-seeded; handles are not.
        Assert.Equal(
            "https://acme.chargify.com/product_families/handle:eshop-subscribe/products.json?per_page=200",
            LastUri.ToString());

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", product.ProductFamily?.Handle);
    }

    [Fact]
    public async Task EscapesTheCustomerReferenceInTheLookupQuery()
    {
        _handler.Respond(HttpStatusCode.OK, """{"customer":{"id":7,"reference":"eshoponweb-a+b@example.com"}}""");

        var customer = await CreateClient().FindCustomerByReferenceAsync("eshoponweb-a+b@example.com");

        Assert.Equal(7, customer!.Id);
        Assert.Contains("reference=eshoponweb-a%2Bb%40example.com", LastUri.ToString());
    }

    [Fact]
    public async Task ReturnsNullWhenNoCustomerHasTheReference()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().FindCustomerByReferenceAsync("nobody"));
    }

    [Fact]
    public async Task ReturnsNullWhenNoSubscriptionHasTheReference()
    {
        _handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await CreateClient().FindSubscriptionByReferenceAsync("nothing"));
    }

    [Fact]
    public async Task SerialisesTheCustomerPayloadInSnakeCaseAndOmitsNulls()
    {
        _handler.Respond(HttpStatusCode.Created, """{"customer":{"id":7,"reference":"ref"}}""");

        await CreateClient().CreateCustomerAsync(new MaxioCreateCustomer
        {
            FirstName = "Demo",
            LastName = "User",
            Email = "demo@example.com",
            Reference = "ref"
        });

        var body = _handler.RequestBodies[^1]!;
        Assert.Equal("https://acme.chargify.com/customers.json", LastUri.ToString());
        Assert.Contains("\"first_name\":\"Demo\"", body);
        Assert.Contains("\"last_name\":\"User\"", body);
        Assert.Contains("\"reference\":\"ref\"", body);
        Assert.DoesNotContain("organization", body);
    }

    [Fact]
    public async Task SerialisesTheSubscriptionPayloadMaxioExpects()
    {
        _handler.Respond(HttpStatusCode.Created, """
            {"subscription":{"id":3,"state":"active","currency":"USD","balance_in_cents":29900,
             "next_assessment_at":"2026-10-06T14:12:06+05:00",
             "product":{"handle":"eshop-pro","name":"Pro Plan"},"customer":{"id":42}}}
            """);

        var subscription = await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 42,
            Reference = "eshoponweb-demo:eshop-pro",
            PaymentCollectionMethod = MaxioCollectionMethods.Remittance
        });

        var body = _handler.RequestBodies[^1]!;
        Assert.Equal("https://acme.chargify.com/subscriptions.json", LastUri.ToString());
        Assert.Contains("\"subscription\":{", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":42", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);

        Assert.Equal(3, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 14, 12, 6, TimeSpan.FromHours(5)), subscription.NextAssessmentAt);
        Assert.Equal(42, subscription.Customer?.Id);
    }

    [Fact]
    public async Task ListsSubscriptionsForACustomer()
    {
        _handler.Respond(HttpStatusCode.OK, """[{"subscription":{"id":3,"state":"active"}}]""");

        var subscriptions = await CreateClient().ListCustomerSubscriptionsAsync(42);

        Assert.Equal("https://acme.chargify.com/customers/42/subscriptions.json", LastUri.ToString());
        Assert.Equal(3, Assert.Single(subscriptions).Id);
    }

    [Fact]
    public async Task TranslatesAValidationFailureIntoADuplicateReferenceSignal()
    {
        _handler.Respond(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscription()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsDuplicateReference);
        Assert.Contains("Reference: must be unique - that value has been taken.", exception.Errors);
    }

    [Fact]
    public async Task DoesNotMistakeOtherValidationFailuresForDuplicateReferences()
    {
        _handler.Respond(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscription()));

        Assert.False(exception.IsDuplicateReference);
    }

    [Fact]
    public async Task SurfacesAnAuthenticationFailure()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().GetSiteAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("Access denied", string.Join(" ", exception.Errors));
    }

    [Fact]
    public async Task RejectsASuccessResponseThatIsMissingItsEnvelope()
    {
        _handler.Respond(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().GetSiteAsync());
    }
}
