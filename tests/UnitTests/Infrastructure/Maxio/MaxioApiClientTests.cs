using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Verifies the client speaks exactly what maxio-spec/openapi.yaml declares: the request lines, the
/// auth scheme, and the response envelopes.
/// </summary>
public class MaxioApiClientTests
{
    [Fact]
    public async Task ListsProductsByFamilyHandleUsingTheHandlePrefix()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, """
            [{"product":{"id":1,"name":"Pro Plan","handle":"pro-plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,
              "product_family":{"id":9,"handle":"demo-family","name":"Demo Family"}}}]
            """);

        var products = await MaxioTestFactory.Client(handler).ListProductsForProductFamilyAsync("demo-family");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle%3Ademo-family/products.json", request.Uri.AbsolutePath);
        Assert.Contains("per_page=200", request.Uri.Query);
        Assert.Contains("include_archived=false", request.Uri.Query);

        var product = Assert.Single(products);
        Assert.Equal("pro-plan", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("demo-family", product.ProductFamily?.Handle);
    }

    [Fact]
    public async Task FollowsPagesUntilOneComesBackShort()
    {
        var fullPage = "[" + string.Join(',', Enumerable.Range(1, 2).Select(id =>
            $$$"""{"product":{"id":{{{id}}},"handle":"plan-{{{id}}}","name":"Plan {{{id}}}","price_in_cents":100,"interval":1,"interval_unit":"month"}}""")) + "]";

        var handler = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.OK, fullPage)
            .Respond(HttpStatusCode.OK, """[{"product":{"id":3,"handle":"plan-3","name":"Plan 3","price_in_cents":100,"interval":1,"interval_unit":"month"}}]""");

        var settings = MaxioTestFactory.Settings(s => s.PageSize = 2);
        var products = await MaxioTestFactory.Client(handler, settings).ListProductsForProductFamilyAsync("demo-family");

        Assert.Equal(3, products.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1", handler.Requests[0].Uri.Query);
        Assert.Contains("page=2", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task TreatsACustomerLookupMissAsNull()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.NotFound);

        var customer = await MaxioTestFactory.Client(handler).ReadCustomerByReferenceAsync("eshop-nobody@example.com");

        Assert.Null(customer);
        Assert.Equal("/customers/lookup.json", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task TreatsASubscriptionLookupMissAsNull()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.NotFound);

        var subscription = await MaxioTestFactory.Client(handler).FindSubscriptionByReferenceAsync("key-123");

        Assert.Null(subscription);
        Assert.Equal("/subscriptions/lookup.json", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task PostsTheCreateSubscriptionEnvelopeTheSpecificationDeclares()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Created, """
            {"subscription":{"id":42,"state":"active","product_price_in_cents":29900,
             "next_assessment_at":"2026-10-06T17:22:48+05:00","currency":"USD",
             "payment_collection_method":"remittance",
             "product":{"id":1,"handle":"pro-plan","name":"Pro Plan","interval":1,"interval_unit":"month"},
             "customer":{"id":7,"reference":"eshop-demo@example.com"}}}
            """);

        var subscription = await MaxioTestFactory.Client(handler).CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "pro-plan",
            CustomerId = 7,
            PaymentCollectionMethod = CollectionMethods.Remittance
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions.json", request.Uri.AbsolutePath);
        Assert.Equal(
            """{"subscription":{"product_handle":"pro-plan","customer_id":7,"payment_collection_method":"remittance"}}""",
            request.Body);

        Assert.Equal(42, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("pro-plan", subscription.Product?.Handle);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 17, 22, 48, TimeSpan.FromHours(5)), subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task OmitsAttributesThatWereNotSet()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, """{"customer":{"id":7}}""");

        await MaxioTestFactory.Client(handler).CreateCustomerAsync(new MaxioCreateCustomer
        {
            FirstName = "Demo",
            LastName = "User",
            Email = "demo@example.com",
            Reference = "eshop-demo@example.com"
        });

        var body = Assert.Single(handler.Requests).Body;
        Assert.DoesNotContain("organization", body);
        Assert.Contains(""""reference":"eshop-demo@example.com"""", body);
    }

    [Fact]
    public async Task SurfacesTheErrorMessagesMaxioReturns()
    {
        var handler = new StubHttpMessageHandler().Respond(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => MaxioTestFactory.Client(handler).CreateCustomerAsync(new MaxioCreateCustomer()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal("createCustomer", exception.OperationId);
        Assert.Contains("must be unique", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReportsTransportFailuresSeparatelyFromApiErrors()
    {
        var handler = new StubHttpMessageHandler().Respond(_ => throw new HttpRequestException("connection reset"));

        var exception = await Assert.ThrowsAsync<MaxioTransportException>(
            () => MaxioTestFactory.Client(handler).ListProductsForProductFamilyAsync("demo-family"));

        Assert.Equal("listProductsForProductFamily", exception.OperationId);
    }

    [Fact]
    public async Task ReadsTheArrayEnvelopeOfListCustomerSubscriptions()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, """
            [{"subscription":{"id":1,"state":"active","product":{"handle":"pro-plan"}}},
             {"subscription":{"id":2,"state":"canceled","product":{"handle":"starter-plan"}}}]
            """);

        var subscriptions = await MaxioTestFactory.Client(handler).ListCustomerSubscriptionsAsync(7);

        Assert.Equal("/customers/7/subscriptions.json", Assert.Single(handler.Requests).Uri.AbsolutePath);
        Assert.Equal(new[] { 1, 2 }, subscriptions.Select(s => s.Id));
    }
}
