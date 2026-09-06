using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Wire-level tests: these pin the shapes actually exchanged with Maxio - snake_case naming, the
/// single-property envelopes, and the error payloads - which no amount of service-level testing covers.
/// </summary>
public class MaxioApiClientTests
{
    [Fact]
    public async Task ListProductsForFamilyAsync_AddressesTheFamilyByHandleAndUnwrapsTheEnvelopes()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [{"product":{"id":7130999,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
              "product_price_point_handle":"uuid:abc",
              "product_family":{"id":1,"handle":"eshop-subscribe","name":"eShop Subscribe"}}}]
            """);

        var products = await CreateClient(handler).ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", handler.LastRequestUri!.AbsolutePath);
        Assert.Contains("per_page=200", handler.LastRequestUri.Query);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_EscapesTheReferenceAndReturnsNullOnNotFound()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "");

        var customer = await CreateClient(handler).FindCustomerByReferenceAsync("eshoponweb:a b@example.com");

        Assert.Null(customer);
        Assert.Contains("reference=eshoponweb%3Aa%20b%40example.com", handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsSnakeCaseAttributesWithTheUniquenessTokenBesideThem()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """
            {"subscription":{"id":94209238,"state":"active","product_price_in_cents":2900,
              "balance_in_cents":2900,"payment_collection_method":"remittance",
              "next_assessment_at":"2026-10-06T14:09:07+05:00",
              "current_period_ends_at":"2026-10-06T14:09:07+05:00",
              "product":{"handle":"basic-plan","name":"Basic Plan","interval":1,"interval_unit":"month"},
              "customer":{"id":98837851,"reference":"eshoponweb:demouser@microsoft.com"}}}
            """);

        var subscription = await CreateClient(handler).CreateSubscriptionAsync(
            new MaxioSubscriptionAttributes
            {
                ProductHandle = "basic-plan",
                CustomerId = 98837851,
                PaymentCollectionMethod = "remittance"
            },
            uniquenessToken: "token-123");

        using var sent = JsonDocument.Parse(handler.LastRequestBody!);
        var attributes = sent.RootElement.GetProperty("subscription");

        Assert.Equal("basic-plan", attributes.GetProperty("product_handle").GetString());
        Assert.Equal(98837851, attributes.GetProperty("customer_id").GetInt64());
        Assert.Equal("remittance", attributes.GetProperty("payment_collection_method").GetString());
        Assert.Equal("token-123", sent.RootElement.GetProperty("uniqueness_token").GetString());
        // Unset attributes are omitted rather than sent as explicit nulls.
        Assert.False(attributes.TryGetProperty("customer_reference", out _));

        Assert.Equal(94209238, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(2900, subscription.ProductPriceInCents);
        Assert.Equal("basic-plan", subscription.Product!.Handle);
        Assert.NotNull(subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesADuplicateSubmissionAsAConflict()
    {
        var handler = new StubHandler(
            HttpStatusCode.Conflict, """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler).CreateSubscriptionAsync(new MaxioSubscriptionAttributes(), "token"));

        Assert.True(exception.IsDuplicateSubmission);
        Assert.Equal(409, exception.UpstreamStatusCode);
    }

    [Fact]
    public async Task CreateCustomerAsync_RecognisesATakenReferenceAmongValidationErrors()
    {
        var handler = new StubHandler(
            HttpStatusCode.UnprocessableEntity, """{"errors":["Reference: must be unique."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes(), "token"));

        Assert.True(exception.IndicatesReferenceTaken);
        Assert.False(exception.IsDuplicateSubmission);
    }

    [Fact]
    public async Task SendAsync_ReadsErrorsGivenAsAnObjectKeyedByField()
    {
        var handler = new StubHandler(
            HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"is invalid"}}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes(), "token"));

        Assert.Contains("customer: is invalid", exception.Errors);
    }

    [Fact]
    public async Task SendAsync_ReportsAnUnreachableBillingSystemAsUnavailable()
    {
        var handler = new ThrowingHandler(new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<BillingUnavailableException>(() =>
            CreateClient(handler).GetSiteAsync());
    }

    [Fact]
    public async Task GetSiteAsync_ReadsTheFlagsTheIntegrationDependsOn()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"site":{"id":93063,"subdomain":"cp-exp-4","currency":"USD",
              "relationship_invoicing_enabled":true,"default_payment_collection_method":"automatic","test":true}}
            """);

        var site = await CreateClient(handler).GetSiteAsync();

        Assert.Equal("USD", site!.Currency);
        Assert.True(site.RelationshipInvoicingEnabled);
        Assert.True(site.Test);
    }

    private static MaxioApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") },
            NullLogger<MaxioApiClient>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public Uri? LastRequestUri { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => throw _exception;
    }
}
