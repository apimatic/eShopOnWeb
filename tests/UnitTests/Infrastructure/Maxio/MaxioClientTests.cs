using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioClientTests
{
    private static MaxioClient CreateClient(FakeHttpHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };
        return new MaxioClient(httpClient, options, NullLogger<MaxioClient>.Instance);
    }

    [Theory]
    [InlineData("{\"errors\":[\"First name: cannot be blank.\"]}", "First name: cannot be blank.")]
    [InlineData("{\"errors\":{\"subscription\":\"Reference has already been taken.\"}}", "subscription: Reference has already been taken.")]
    [InlineData("{\"error\":\"Not found\"}", "Not found")]
    public void ParseErrorsNormalizesAllSpecErrorShapes(string body, string expectedMessage)
    {
        var errors = MaxioClient.ParseErrors(body, "fallback");

        Assert.Single(errors);
        Assert.Equal(expectedMessage, errors[0]);
    }

    [Fact]
    public void ParseErrorsFallsBackToRawBodyForNonJsonPayloads()
    {
        var errors = MaxioClient.ParseErrors("<html>gateway timeout</html>", "fallback");

        Assert.Single(errors);
        Assert.Equal("<html>gateway timeout</html>", errors[0]);
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullOn404()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "{\"error\":\"Not found\"}");
        var client = CreateClient(handler);

        var result = await client.FindCustomerByReferenceAsync("some-ref");

        Assert.Null(result);
    }

    [Fact]
    public async Task FindCustomerByReferenceDeserializesEnvelope()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            "{\"customer\":{\"id\":42,\"first_name\":\"Demo\",\"last_name\":\"User\",\"email\":\"demouser@microsoft.com\",\"reference\":\"ref-1\"}}");
        var client = CreateClient(handler);

        var result = await client.FindCustomerByReferenceAsync("ref-1");

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("ref-1", result.Reference);
        Assert.Equal("Demo", result.FirstName);
    }

    [Fact]
    public async Task CreateSubscriptionSendsSnakeCaseEnvelopeWithBasicAuth()
    {
        string? capturedAuth = null;
        string? capturedBody = null;
        var handler = new FakeHttpHandler(HttpStatusCode.Created,
            "{\"subscription\":{\"id\":10,\"state\":\"active\",\"product_price_in_cents\":29900,\"currency\":\"USD\"}}",
            responseCallback: _ => { },
            requestCallback: request =>
            {
                capturedAuth = request.Headers.Authorization?.ToString();
                capturedBody = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            });
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(
            new MaxioCreateSubscription { ProductHandle = "eshop-pro", CustomerId = 42, Reference = "ref:plan" });

        Assert.Equal(10, result.Id);
        Assert.Equal("Basic dGVzdC1rZXk6eA==", capturedAuth);
        Assert.Equal("{\"subscription\":{\"product_handle\":\"eshop-pro\",\"customer_id\":42,\"reference\":\"ref:plan\",\"payment_collection_method\":\"remittance\"}}", capturedBody);
    }

    [Fact]
    public async Task TransientServerErrorIsRetried()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");
        handler.Responses.Add(new FakeResponse(HttpStatusCode.OK,
            "{\"subscription\":{\"id\":10,\"state\":\"active\"}}"));
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(new MaxioCreateSubscription { ProductHandle = "p", CustomerId = 1 });

        Assert.Equal(10, result.Id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PersistentConflictThrowsMaxioApiExceptionWithParsedErrors()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.UnprocessableEntity,
            "{\"errors\":[\"Reference has already been taken.\"]}");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscription { ProductHandle = "p", CustomerId = 1 }));

        Assert.Equal(422, exception.StatusCode);
        Assert.True(exception.IsConflict);
        Assert.Contains("Reference has already been taken.", exception.Errors);
    }

    private sealed record FakeResponse(HttpStatusCode StatusCode, string Body);

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly FakeResponse _first;
        private readonly List<FakeResponse> _responses = new();
        private readonly Action<HttpResponseMessage>? _responseCallback;
        private readonly Action<HttpRequestMessage>? _requestCallback;

        public FakeHttpHandler(
            HttpStatusCode statusCode,
            string body,
            Action<HttpResponseMessage>? responseCallback = null,
            Action<HttpRequestMessage>? requestCallback = null)
        {
            _first = new FakeResponse(statusCode, body);
            _responseCallback = responseCallback;
            _requestCallback = requestCallback;
        }

        public List<FakeResponse> Responses => _responses;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            _requestCallback?.Invoke(request);

            var fake = Requests.Count == 1 ? _first : _responses.Count >= Requests.Count - 1 ? _responses[Requests.Count - 2] : _first;
            var response = new HttpResponseMessage(fake.StatusCode)
            {
                Content = new StringContent(fake.Body, Encoding.UTF8, "application/json")
            };
            _responseCallback?.Invoke(response);
            return Task.FromResult(response);
        }
    }
}