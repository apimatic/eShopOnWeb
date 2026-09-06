#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class MaxioApiClientTests
{
    private readonly StubHttpMessageHandler _handler = new();

    private MaxioApiClient CreateClient()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-key:X"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandleAndAuthenticatesWithBasicAuth()
    {
        _handler.Respond(HttpStatusCode.OK,
            """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":7,"handle":"eshop-subscribe"}}}]""");

        var products = await CreateClient().ListProductsForFamilyAsync("eshop-subscribe");

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json", request.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("dGVzdC1rZXk6WA==", request.Headers.Authorization.Parameter);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task PagesUntilAShortPageComesBack()
    {
        var fullPage = "[" + string.Join(",",
            Enumerable.Range(1, 200).Select(i =>
                "{\"product\":{\"id\":" + i + ",\"handle\":\"plan-" + i + "\"}}")) + "]";

        _handler.Respond(HttpStatusCode.OK, fullPage)
            .Respond(HttpStatusCode.OK, """[{"product":{"id":999,"handle":"last"}}]""");

        var products = await CreateClient().ListProductsForFamilyAsync("family");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Contains("page=1", _handler.Requests[0].RequestUri!.Query);
        Assert.Contains("page=2", _handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task AMissingCustomerLookupIsAnEmptyAnswerNotAFailure()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""");

        var customer = await CreateClient().FindCustomerByReferenceAsync("eshoponweb-nobody@example.com");

        Assert.Null(customer);
        Assert.Contains("reference=eshoponweb-nobody%40example.com", _handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task SendsTheUniquenessTokenAlongsideTheResourceKey()
    {
        _handler.Respond(HttpStatusCode.OK, """{"subscription":{"id":42,"state":"active"}}""");

        await CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = "eshop-pro",
                CustomerId = 7,
                PaymentCollectionMethod = "remittance"
            },
            UniquenessToken = "subscribe-abc"
        });

        var body = Assert.Single(_handler.RequestBodies)!;
        Assert.Contains("\"uniqueness_token\":\"subscribe-abc\"", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":7", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        // Null members are omitted rather than sent as explicit nulls.
        Assert.DoesNotContain("product_price_point_handle", body);
    }

    [Fact]
    public async Task ARejectedRequestSurfacesTheProviderMessages()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<BillingValidationException>(() =>
            CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest()));

        Assert.Contains("No payment method was on file", exception.Message);
        Assert.Equal("No payment method was on file for the $299.00 balance", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task FieldKeyedErrorObjectsAreFlattenedToo()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"must be unique"}}""");

        var exception = await Assert.ThrowsAsync<BillingValidationException>(() =>
            CreateClient().CreateCustomerAsync(new CreateCustomerRequest()));

        Assert.Equal("customer: must be unique", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ADuplicateSubmissionIsDistinguishableFromOtherFailures()
    {
        _handler.Respond(HttpStatusCode.Conflict, """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""");

        await Assert.ThrowsAsync<BillingConflictException>(() =>
            CreateClient().CreateSubscriptionAsync(new CreateSubscriptionRequest()));
    }

    [Fact]
    public async Task RejectedCredentialsAreReportedAsAProviderFailureWithAPointerToTheSetting()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => CreateClient().ReadSiteAsync());

        Assert.Equal(401, exception.StatusCode);
        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task AnUnreachableProviderIsAProviderFailureNotACallerMistake()
    {
        _handler.Throw(new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => CreateClient().ReadSiteAsync());

        Assert.Contains("Could not reach Maxio", exception.Message);
    }

    [Fact]
    public async Task ReadsTheSiteCurrencyAndBillingArchitecture()
    {
        _handler.Respond(HttpStatusCode.OK,
            """{"site":{"id":1,"subdomain":"acme","currency":"USD","test":true,"relationship_invoicing_enabled":true}}""");

        var site = await CreateClient().ReadSiteAsync();

        Assert.Equal("USD", site!.Currency);
        Assert.True(site.RelationshipInvoicingEnabled);
    }
}
