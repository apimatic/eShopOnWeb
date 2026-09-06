using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientTests
{
    [Fact]
    public void ParsesTheArrayErrorShape()
    {
        var errors = MaxioApiClient.ParseErrors("{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, errors);
    }

    [Fact]
    public void ParsesTheKeyedObjectErrorShape()
    {
        var errors = MaxioApiClient.ParseErrors("{\"errors\":{\"customer\":\"is invalid\"}}");

        Assert.Equal(new[] { "customer: is invalid" }, errors);
    }

    [Fact]
    public void FallsBackToTheRawTextWhenTheBodyIsNotJson()
    {
        var errors = MaxioApiClient.ParseErrors("HTTP Basic: Access denied.\n");

        Assert.Equal(new[] { "HTTP Basic: Access denied." }, errors);
    }

    [Fact]
    public void ReturnsNothingWhenThereIsNoErrorPayload()
    {
        Assert.Empty(MaxioApiClient.ParseErrors("{\"subscription\":{\"id\":1}}"));
        Assert.Empty(MaxioApiClient.ParseErrors(""));
        Assert.Empty(MaxioApiClient.ParseErrors(null));
    }

    [Fact]
    public async Task ALookupThatFindsNothingReturnsNullRatherThanThrowing()
    {
        var client = BuildClient(_ => (HttpStatusCode.NotFound, ""));

        Assert.Null(await client.FindCustomerByReferenceAsync("eshoponweb:nobody@example.com"));
    }

    [Fact]
    public async Task AFailedCallSurfacesMaxiosOwnMessages()
    {
        var client = BuildClient(_ => (HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Product with API Handle 'nope' does not exist for this site.\"]}"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(new MaxioSubscriptionAttributes { ProductHandle = "nope" }, uniquenessToken: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsRejection);
        Assert.Contains("does not exist for this site", exception.Message);
    }

    [Fact]
    public async Task ADuplicateSubmissionIsRecognised()
    {
        var client = BuildClient(_ => (HttpStatusCode.Conflict, "{\"errors\":[\"DuplicatePrevention::DuplicateSubmissionError\"]}"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateSubscriptionAsync(new MaxioSubscriptionAttributes { ProductHandle = "eshop-pro" }, "a-key"));

        Assert.True(exception.IsDuplicateSubmission);
        Assert.False(exception.IsReferenceAlreadyTaken);
    }

    [Fact]
    public async Task ATakenReferenceIsRecognised()
    {
        var client = BuildClient(_ => (HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            client.CreateCustomerAsync(new MaxioCustomerAttributes { FirstName = "A", LastName = "B", Email = "a@b.com" }));

        Assert.True(exception.IsReferenceAlreadyTaken);
    }

    [Fact]
    public async Task AnUnreachableApiIsReportedAsATransportFailure()
    {
        var client = new MaxioApiClient(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://test-site.chargify.com/") },
            NullLogger<MaxioApiClient>.Instance);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => client.ReadSiteAsync());

        Assert.True(exception.IsTransportFailure);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task PagesThroughProductsUntilAShortPageArrives()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 200).Select(i =>
            $"{{\"product\":{{\"id\":{i},\"handle\":\"plan-{i}\",\"name\":\"Plan {i}\",\"price_in_cents\":100,\"interval\":1,\"interval_unit\":\"month\"}}}}")) + "]";

        var pages = 0;
        var client = BuildClient(_ => ++pages == 1
            ? (HttpStatusCode.OK, fullPage)
            : (HttpStatusCode.OK, "[{\"product\":{\"id\":999,\"handle\":\"last-plan\",\"name\":\"Last\",\"price_in_cents\":100,\"interval\":1,\"interval_unit\":\"month\"}}]"));

        var products = await client.ListProductsForFamilyAsync("demo-plans");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, pages);
    }

    private static MaxioApiClient BuildClient(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
    {
        var handler = new InlineHandler(respond);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };
        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    private sealed class InlineHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _respond;

        public InlineHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = _respond(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }
}
