using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioApiClientTests
{
    private static (MaxioApiClient Client, RecordingHandler Handler) CreateClient(MaxioOptions? options = null)
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new MaxioApiClient(
            httpClient,
            new TestOptionsMonitor<MaxioOptions>(options ?? TestOptions.Valid()),
            NullLogger<MaxioApiClient>.Instance);

        return (client, handler);
    }

    [Fact]
    public async Task ListProductsAddressesTheProductFamilyByHandle()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var products = await client.ListProductsForProductFamilyAsync("handle:eshop-subscribe");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/product_families/handle:eshop-subscribe/products.json",
            Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
        Assert.Equal("acme.chargify.com", request.RequestUri.Host);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
    }

    [Fact]
    public async Task RequestsAuthenticateWithBasicAuthUsingTheApiKeyAndX()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        await client.ListProductsForProductFamilyAsync("handle:eshop-subscribe");

        var authorization = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal(
            "api-key:x",
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task CustomerLookupSendsTheReferenceAsAQueryParameterAndTreatsNotFoundAsNoCustomer()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.NotFound);

        var customer = await client.ReadCustomerByReferenceAsync("eshoponweb:user:demouser@microsoft.com");

        Assert.Null(customer);
        Assert.Equal("/customers/lookup.json", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(
            "?reference=eshoponweb%3Auser%3Ademouser%40microsoft.com",
            handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task SubscriptionLookupReturnsTheSubscriptionWhenFound()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.OK, """{"subscription":{"id":42,"state":"active","reference":"r","product":{"handle":"eshop-pro"}}}""");

        var subscription = await client.FindSubscriptionAsync("r");

        Assert.NotNull(subscription);
        Assert.Equal(42, subscription!.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
    }

    [Fact]
    public async Task CreateSubscriptionSendsTheSpecEnvelopeAndOmitsUnsetFields()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.Created, """{"subscription":{"id":7,"state":"active"}}""");

        var subscription = await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 5,
            Reference = "eshoponweb:sub:demouser@microsoft.com:eshop-pro",
            PaymentCollectionMethod = "remittance"
        });

        Assert.Equal(7, subscription.Id);
        Assert.Equal("/subscriptions.json", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(
            """{"subscription":{"product_handle":"eshop-pro","customer_id":5,"reference":"eshoponweb:sub:demouser@microsoft.com:eshop-pro","payment_collection_method":"remittance"}}""",
            handler.Bodies[0]);
    }

    [Fact]
    public async Task DuplicateReferenceRejectionsAreRecognisable()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer { Email = "a@b.com" }));

        Assert.True(exception.IsDuplicateReference);
        Assert.True(exception.IsClientError);
        Assert.Contains("Reference: must be unique - that value has been taken.", exception.Errors);
    }

    [Fact]
    public async Task ErrorsReportedAsAFieldMapAreSurfaced()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"can't be blank"}}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer()));

        Assert.Contains("customer: can't be blank", exception.Errors);
    }

    [Fact]
    public async Task NotFoundIsAnErrorForOperationsThatDoNotDocumentIt()
    {
        var (client, handler) = CreateClient();
        handler.Enqueue(HttpStatusCode.NotFound, "\"A valid product_family_id is required\"");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.ListProductsForProductFamilyAsync("handle:missing"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task MissingConfigurationFailsBeforeAnyRequestIsSent()
    {
        var (client, handler) = CreateClient(new MaxioOptions());

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.ListProductsForProductFamilyAsync("handle:eshop-subscribe"));

        Assert.Empty(handler.Requests);
    }
}
