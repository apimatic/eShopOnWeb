using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioApiClientTests
{
    private static (MaxioApiClient Client, StubHttpMessageHandler Handler) Build(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example-site.chargify.com/") };
        return (new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance), handler);
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandleRatherThanByNumericId()
    {
        var (client, handler) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, "[]"));

        await client.ListProductsForProductFamilyAsync("eshop-subscribe");

        var uri = handler.Requests.Single().Uri;
        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json", uri.AbsolutePath);
        Assert.Contains("per_page=200", uri.Query);
    }

    [Fact]
    public async Task UnwrapsTheProductEnvelopeTheSpecificationDefines()
    {
        const string body = """
        [
          { "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "archived_at": null, "product_price_point_name": "Original" } }
        ]
        """;

        var (client, _) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, body));

        var products = await client.ListProductsForProductFamilyAsync("eshop-subscribe");

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
    }

    [Fact]
    public async Task StopsPagingOnceAPartialPageComesBack()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, "[]");
        var (client, _) = Build(handler);

        await client.ListProductsForProductFamilyAsync("eshop-subscribe");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReportsAMissingCustomerAsNullRatherThanAsAFailure()
    {
        var (client, _) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound));

        Assert.Null(await client.ReadCustomerByReferenceAsync("eshoponweb--nobody"));
    }

    [Fact]
    public async Task ReportsAMissingSubscriptionAsNullRatherThanAsAFailure()
    {
        var (client, _) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound));

        Assert.Null(await client.FindSubscriptionAsync("eshoponweb--nobody--eshop-pro"));
    }

    [Fact]
    public async Task SurfacesTheErrorMessagesFromAnErrorListResponse()
    {
        const string body = """{"errors":["Reference: must be unique - that value has been taken."]}""";
        var (client, _) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.UnprocessableEntity, body));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscription { ProductHandle = "eshop-pro" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Contains("must be unique", Assert.Single(exception.Errors));
        Assert.True(exception.IsDuplicateReference);
    }

    [Fact]
    public async Task SurfacesTheErrorMessagesFromAFieldKeyedCustomerErrorResponse()
    {
        const string body = """{"errors":{"customer":"can't be blank"}}""";
        var (client, _) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.UnprocessableEntity, body));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer()));

        Assert.Equal("customer: can't be blank", Assert.Single(exception.Errors));
        Assert.False(exception.IsDuplicateReference);
    }

    [Fact]
    public async Task DoesNotMistakeAnUnrelatedValidationFailureForADuplicateReference()
    {
        const string body = """{"errors":["No payment method was on file for the $299.00 balance"]}""";
        var (client, _) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.UnprocessableEntity, body));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscription { ProductHandle = "eshop-pro" }));

        Assert.False(exception.IsDuplicateReference);
    }

    [Fact]
    public async Task ReportsAnUnreachableBillingProviderAsABillingProviderFailure()
    {
        var handler = new ThrowingHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example-site.chargify.com/") };
        var client = new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);

        await Assert.ThrowsAsync<BillingProviderException>(() => client.ReadSiteAsync());
    }

    [Fact]
    public async Task SendsTheSubscriptionBodyInTheSnakeCasedShapeTheSpecificationDefines()
    {
        const string body = """{"subscription":{"id":5,"state":"active"}}""";
        var (client, handler) = Build(new StubHttpMessageHandler().Enqueue(HttpStatusCode.Created, body));

        await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 42,
            Reference = "eshoponweb--demo--eshop-pro",
            PaymentCollectionMethod = "remittance"
        });

        var sent = handler.Requests.Single().Body;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", sent);
        Assert.Contains("\"customer_id\":42", sent);
        Assert.Contains("\"reference\":\"eshoponweb--demo--eshop-pro\"", sent);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", sent);
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
