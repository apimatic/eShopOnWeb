using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientTests
{
    private static (MaxioApiClient Client, StubHttpMessageHandler Handler) Build()
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };

        return (new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance), handler);
    }

    [Fact]
    public async Task ListProductsForProductFamilyBuildsTheSpecifiedPathAndPaging()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """[{"product":{"id":1,"handle":"pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var products = await client.ListProductsForProductFamilyAsync("handle:eshop-subscribe", page: 2, perPage: 200);

        Assert.Equal(
            "https://acme.chargify.com/product_families/handle:eshop-subscribe/products.json?page=2&per_page=200",
            handler.Requests[0].RequestUri!.ToString());

        var product = Assert.Single(products);
        Assert.Equal("pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
    }

    [Fact]
    public async Task ReadCustomerByReferenceEscapesTheReferenceAndReturnsTheUnwrappedCustomer()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, """{"customer":{"id":42,"email":"a@b.com","reference":"eshoponweb:a@b.com"}}""");

        var customer = await client.ReadCustomerByReferenceAsync("eshoponweb:a@b.com");

        Assert.Equal(
            "https://acme.chargify.com/customers/lookup.json?reference=eshoponweb%3Aa%40b.com",
            handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(42, customer!.Id);
    }

    [Fact]
    public async Task LookupTreatsNotFoundAsNoSuchRecordRatherThanAFailure()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.NotFound);

        Assert.Null(await client.ReadCustomerByReferenceAsync("missing"));
    }

    [Fact]
    public async Task FailureCarriesTheStatusCodeAndTheProviderErrorList()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["Product with API Handle 'nope' does not exist for this site."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscription { ProductHandle = "nope" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsValidationFailure);
        Assert.Equal("Product with API Handle 'nope' does not exist for this site.", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task FailureFlattensTheKeyedErrorShapeTheSpecificationAlsoDefines()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"can't be blank"}}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer()));

        Assert.Equal("customer: can't be blank", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task FailureSurvivesANonJsonErrorBody()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer()));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.False(exception.IsValidationFailure);
        Assert.Contains("HTTP Basic: Access denied.", exception.Message);
    }

    [Fact]
    public async Task UnreachableProviderBecomesAMaxioApiExceptionRatherThanAnHttpException()
    {
        var (client, handler) = Build();
        handler.EnqueueThrow(new HttpRequestException("no route to host"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.ReadCustomerByReferenceAsync("anything"));

        Assert.Null(exception.StatusCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task CreateSubscriptionSendsTheSpecifiedEnvelopeAndOmitsUnsetProperties()
    {
        var (client, handler) = Build();
        handler.Enqueue(HttpStatusCode.Created, """{"subscription":{"id":7,"state":"active"}}""");

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 42,
            PaymentCollectionMethod = "remittance"
        });

        Assert.Equal(
            """{"subscription":{"product_handle":"eshop-pro","customer_id":42,"payment_collection_method":"remittance"}}""",
            handler.RequestBodies[0]);
        Assert.Equal(7, subscription.Id);
    }
}
