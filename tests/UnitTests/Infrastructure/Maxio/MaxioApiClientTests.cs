using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Pins the wire contract this integration was built against. The payloads below are trimmed copies
/// of real Maxio responses, so a change in how requests are shaped or responses are read shows up
/// here rather than against the live billing site.
/// </summary>
public class MaxioApiClientTests
{
    [Fact]
    public async Task ListProductsForFamilyAddressesTheFamilyByHandleAndPagesTheResults()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            [
              {"product":{"id":7130997,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
                          "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
                          "product_family":{"id":3026730,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}
            ]
            """);

        var products = await CreateClient(handler).ListProductsForFamilyAsync("eshop-subscribe");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", request.Uri!.AbsolutePath);
        Assert.Contains("page=1", request.Uri.Query);
        Assert.Contains("per_page=200", request.Uri.Query);

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task ListEndpointsKeepPagingWhileFullPagesComeBack()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, FullPageOfSubscriptions());
        handler.Enqueue(HttpStatusCode.OK, """[{"subscription":{"id":2,"state":"active"}}]""");

        var subscriptions = await CreateClient(handler).ListCustomerSubscriptionsAsync(98837358);

        Assert.Equal(201, subscriptions.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1", handler.Requests[0].Uri!.Query);
        Assert.Contains("page=2", handler.Requests[1].Uri!.Query);
        Assert.Equal("/customers/98837358/subscriptions.json", handler.Requests[0].Uri!.AbsolutePath);
    }

    [Fact]
    public async Task LookupReturnsNullWhenMaxioAnswersNotFound()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, string.Empty);

        var customer = await CreateClient(handler).FindCustomerByReferenceAsync("eshop-nobody");

        Assert.Null(customer);
        Assert.Equal("/customers/lookup.json", handler.Requests[0].Uri!.AbsolutePath);
        Assert.Contains("reference=eshop-nobody", handler.Requests[0].Uri!.Query);
    }

    [Fact]
    public async Task CreateCustomerSendsMaxiosSnakeCasedEnvelope()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Created, """
            {"customer":{"id":98837358,"first_name":"Demo","last_name":"Shopper",
                         "email":"demouser@microsoft.com","reference":"eshop-demouser"}}
            """);

        var customer = await CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes
        {
            FirstName = "Demo",
            LastName = "Shopper",
            Email = "demouser@microsoft.com",
            Reference = "eshop-demouser"
        });

        var body = JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("customer");
        Assert.Equal("Demo", body.GetProperty("first_name").GetString());
        Assert.Equal("Shopper", body.GetProperty("last_name").GetString());
        Assert.Equal("eshop-demouser", body.GetProperty("reference").GetString());
        Assert.Equal(98837358, customer.Id);
    }

    [Fact]
    public async Task CreateSubscriptionSendsTheProductHandleCustomerAndCollectionMethod()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Created, """
            {"subscription":{"id":94208577,"state":"active","reference":"eshop-demouser-eshop-pro",
                             "currency":"USD","product_price_in_cents":29900,"balance_in_cents":29900,
                             "payment_collection_method":"remittance",
                             "current_period_ends_at":"2026-10-06T11:23:22+05:00",
                             "next_assessment_at":"2026-10-06T11:23:22+05:00",
                             "product":{"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"},
                             "customer":{"id":98837358,"reference":"eshop-demouser"}}}
            """);

        var subscription = await CreateClient(handler).CreateSubscriptionAsync(new MaxioSubscriptionAttributes
        {
            ProductHandle = "eshop-pro",
            CustomerId = 98837358,
            PaymentCollectionMethod = "remittance",
            Reference = "eshop-demouser-eshop-pro"
        });

        Assert.Equal("/subscriptions.json", handler.Requests[0].Uri!.AbsolutePath);

        var body = JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
        Assert.Equal(98837358, body.GetProperty("customer_id").GetInt64());
        Assert.Equal("remittance", body.GetProperty("payment_collection_method").GetString());

        Assert.Equal(94208577, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-06T11:23:22+05:00"),
            subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task ATakenReferenceIsRecognisableAsSuch()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateSubscriptionAsync(new MaxioSubscriptionAttributes { Reference = "taken" }));

        Assert.True(exception.IsReferenceAlreadyTaken);
        Assert.True(exception.IsCallerError);
        Assert.Contains("must be unique", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task OtherValidationFailuresAreNotMistakenForAReferenceConflict()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["Email address: cannot be blank."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes()));

        Assert.False(exception.IsReferenceAlreadyTaken);
        Assert.Contains("Email address: cannot be blank.", exception.Message);
    }

    [Fact]
    public async Task RejectedCredentialsProduceAnActionableMessageWithoutEchoingTheKey()
    {
        // Maxio answers auth failures with plain text, not JSON.
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.\n");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient(handler).GetSiteAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.DoesNotContain("test-key", exception.Message);
    }

    [Fact]
    public async Task EveryRequestCarriesTheBasicCredentialMaxioExpects()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"site":{"id":1,"subdomain":"acme","currency":"USD"}}""");

        await CreateClient(handler).GetSiteAsync();

        var authorization = handler.Requests[0].Authorization;
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal("test-key:x", Encoding.ASCII.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsAProviderFailureNotATransportError()
    {
        // Every way a Maxio call can fail has to arrive as one exception type, so the layer above
        // can map it without knowing anything about HttpClient.
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(_ => throw new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient(handler).GetSiteAsync());

        Assert.Contains("Could not reach Maxio", exception.Message);
        Assert.False(exception.IsCallerError);
        Assert.False(exception.IsReferenceAlreadyTaken);
    }

    private static MaxioApiClient CreateClient(StubHttpMessageHandler handler)
    {
        var settings = new MaxioSettings { ApiKey = "test-key", Subdomain = "acme", ProductFamilyHandle = "eshop-subscribe" };

        var httpClient = new HttpClient(handler) { BaseAddress = settings.ResolveBaseAddress() };
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    private static string FullPageOfSubscriptions() =>
        "[" + string.Join(",", Enumerable.Range(1, 200).Select(i =>
            "{\"subscription\":{\"id\":" + i + ",\"state\":\"active\"}}")) + "]";
}
