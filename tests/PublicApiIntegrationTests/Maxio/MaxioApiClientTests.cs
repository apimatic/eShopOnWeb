using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioApiClient"/> against canned HTTP responses (no live Maxio
/// calls), covering request shaping and response parsing for the endpoints defined in
/// maxio-spec/openapi.yaml.
/// </summary>
[TestClass]
public class MaxioApiClientTests
{
    private static MaxioApiClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new System.Uri("https://example-site.chargify.com/") };
        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [TestMethod]
    public async Task LookupCustomerByReferenceAsync_ReturnsNull_WhenMaxioRespondsNotFound()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.LookupCustomerByReferenceAsync("someone@example.com");

        Assert.IsNull(result);
        StringAssert.Contains(handler.Requests.Single().RequestUri!.ToString(), "customers/lookup.json?reference=someone%40example.com");
    }

    [TestMethod]
    public async Task LookupCustomerByReferenceAsync_ReturnsCustomer_WhenFound()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, "{\"customer\":{\"id\":42,\"reference\":\"user@example.com\",\"first_name\":\"Ada\",\"last_name\":\"Lovelace\",\"email\":\"user@example.com\"}}"));
        var client = CreateClient(handler);

        var result = await client.LookupCustomerByReferenceAsync("user@example.com");

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result!.Id);
        Assert.AreEqual("Ada", result.FirstName);
        Assert.AreEqual("user@example.com", result.Reference);
    }

    [TestMethod]
    public async Task CreateCustomerAsync_PostsExpectedEnvelope_AndParsesResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, "{\"customer\":{\"id\":7,\"reference\":\"user@example.com\"}}"));
        var client = CreateClient(handler);

        var result = await client.CreateCustomerAsync(new CreateMaxioCustomerRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "user@example.com",
            Reference = "user@example.com"
        });

        Assert.AreEqual(7, result.Id);
        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        StringAssert.EndsWith(request.RequestUri!.ToString(), "customers.json");

        var body = await request.Content!.ReadAsStringAsync();
        StringAssert.Contains(body, "\"first_name\":\"Ada\"");
        StringAssert.Contains(body, "\"reference\":\"user@example.com\"");
    }

    [TestMethod]
    public async Task CreateCustomerAsync_FallsBackToLookup_WhenReferenceAlreadyTaken()
    {
        // Simulates a double-click race: the create call 422s because another request
        // already created the customer for this reference; the client should recover by
        // looking the existing customer up rather than surfacing the error.
        var handler = new FakeHttpMessageHandler(request =>
            request.Method == HttpMethod.Post
                ? JsonResponse(HttpStatusCode.UnprocessableEntity, "{\"errors\":{\"reference\":[\"has already been taken\"]}}")
                : JsonResponse(HttpStatusCode.OK, "{\"customer\":{\"id\":99,\"reference\":\"user@example.com\"}}"));
        var client = CreateClient(handler);

        var result = await client.CreateCustomerAsync(new CreateMaxioCustomerRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "user@example.com",
            Reference = "user@example.com"
        });

        Assert.AreEqual(99, result.Id);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task CreateCustomerAsync_Throws_WhenErrorIsNotAReferenceRace()
    {
        var handler = new FakeHttpMessageHandler(request =>
            request.Method == HttpMethod.Post
                ? JsonResponse(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Email: cannot be blank.\"]}")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        await Assert.ThrowsExceptionAsync<MaxioApiException>(() => client.CreateCustomerAsync(new CreateMaxioCustomerRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = string.Empty,
            Reference = "user@example.com"
        }));
    }

    [TestMethod]
    public async Task ListProductsForFamilyAsync_UsesHandlePrefixedPath_AndUnwrapsEnvelopes()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            "[{\"product\":{\"id\":1,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]"));
        var client = CreateClient(handler);

        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual("eshop-pro", products[0].Handle);
        Assert.AreEqual(29900, products[0].PriceInCents);
        StringAssert.Contains(handler.Requests.Single().RequestUri!.ToString(), "product_families/handle:eshop-subscribe/products.json");
    }

    [TestMethod]
    public async Task CreateSubscriptionAsync_DefaultsToRemittanceCollection_SoNoCardIsRequired()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Created,
            "{\"subscription\":{\"id\":555,\"state\":\"active\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900}}}"));
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
        {
            ProductHandle = "eshop-pro",
            CustomerReference = "user@example.com"
        });

        Assert.AreEqual(555, result.Id);
        Assert.AreEqual("active", result.State);

        var body = await handler.Requests.Single().Content!.ReadAsStringAsync();
        StringAssert.Contains(body, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"customer_reference\":\"user@example.com\"");
    }

    [TestMethod]
    public async Task AnyNonSuccessResponse_ThrowsMaxioApiExceptionWithStatusCode()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{\"errors\":[\"boom\"]}"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioApiException>(
            () => client.ListCustomerSubscriptionsAsync(1));

        Assert.AreEqual(500, exception.StatusCode);
        StringAssert.Contains(exception.Message, "boom");
    }
}
