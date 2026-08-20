using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class MaxioClientContractTests
{
    [TestMethod]
    public async Task UsesSpecServerBasicAuthPathsAndSnakeCaseSchemas()
    {
        var handler = new RecordingHandler(
            "[{\"product\":{\"id\":4,\"name\":\"Pro\",\"handle\":\"pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"product_family\":{\"id\":2,\"handle\":\"family one\"}}}]",
            "{\"customer\":{\"id\":7,\"reference\":\"user-ref\"}}",
            "{\"subscription\":{\"id\":9,\"state\":\"active\",\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2030-02-01T00:00:00Z\",\"currency\":\"USD\",\"reference\":\"sub-ref\",\"customer\":{\"id\":7},\"product\":{\"id\":4,\"name\":\"Pro\",\"handle\":\"pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"product_family\":{\"id\":2,\"handle\":\"family one\"}}}}"
        );
        var client = CreateClient(handler, "https://maxio.test/spec-root/");

        await client.ListProductsAsync("family one", CancellationToken.None);
        await client.CreateCustomerAsync(new MaxioCreateCustomer
        {
            FirstName = "Demo",
            LastName = "User",
            Email = "demo@example.test",
            Reference = "user-ref"
        }, CancellationToken.None);
        await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = "pro",
            CustomerId = 7,
            Reference = "sub-ref"
        }, CancellationToken.None);

        Assert.AreEqual(
            "https://maxio.test/spec-root/product_families/handle:family%20one/products.json",
            handler.Requests[0].Uri);
        Assert.AreEqual("Basic", handler.Requests[0].AuthorizationScheme);
        Assert.AreEqual(new string('k', 12) + ":x", Encoding.UTF8.GetString(Convert.FromBase64String(handler.Requests[0].AuthorizationParameter!)));
        StringAssert.Contains(handler.Requests[1].Body!, "\"first_name\":\"Demo\"");
        StringAssert.Contains(handler.Requests[1].Body!, "\"last_name\":\"User\"");
        StringAssert.Contains(handler.Requests[2].Body!, "\"product_handle\":\"pro\"");
        StringAssert.Contains(handler.Requests[2].Body!, "\"customer_id\":7");
        StringAssert.Contains(handler.Requests[2].Body!, "\"reference\":\"sub-ref\"");
        StringAssert.Contains(handler.Requests[2].Body!, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task MapsSpecErrorListWithoutExposingArbitraryBody()
    {
        var handler = new RecordingHandler("{\"errors\":[\"Product is invalid\"]}")
        {
            StatusCode = HttpStatusCode.UnprocessableEntity
        };
        var client = CreateClient(handler, null);

        var exception = await Assert.ThrowsExceptionAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(new MaxioCreateSubscription(), CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        StringAssert.Contains(exception.Message, "Product is invalid");
        Assert.AreEqual("https://site-name.chargify.com/subscriptions.json", handler.Requests[0].Uri);
    }

    [TestMethod]
    public async Task MapsTransportFailureToStableUpstreamError()
    {
        var client = CreateClient(new ThrowingHandler(), null);

        var exception = await Assert.ThrowsExceptionAsync<MaxioApiException>(() =>
            client.ListProductsAsync("family", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.AreEqual("Maxio could not be reached.", exception.Message);
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler, string? baseUrl) => new(
        new HttpClient(handler),
        Options.Create(new MaxioOptions
        {
            ApiKey = new string('k', 12),
            Subdomain = "site-name",
            ProductFamilyHandle = "family",
            BaseUrl = baseUrl
        }));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public RecordingHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(
        string Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Internal transport detail");
    }
}
