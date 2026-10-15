using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.ToString(), request.Content?.ReadAsStringAsync().Result));
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MaxioGateway Gateway(StubHandler handler, string? baseUrl = null)
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "fam", BaseUrl = baseUrl };
        var client = MaxioServiceCollectionExtensions.CreateClient(new HttpClient(handler), settings, NullLoggerFactory.Instance);
        return new MaxioGateway(client, Options.Create(settings), NullLogger<MaxioGateway>.Instance);
    }

    [Fact]
    public async Task BaseUrlOverrideIsUsedVerbatim()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));
        await Gateway(handler, "https://mock.example.test").ListPlansAsync(default);

        Assert.StartsWith("https://mock.example.test/product_families/handle%3Afam/products.json", handler.Requests[0].Url);
    }

    [Fact]
    public async Task SubdomainDerivesTheHostWhenNoBaseUrlIsSet()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));
        await Gateway(handler).ListPlansAsync(default);

        Assert.StartsWith("https://site.chargify.com/", handler.Requests[0].Url);
    }

    [Fact]
    public async Task PlanListingReportsWhenCappedAtFivePages()
    {
        var page = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => "{\"product\":{\"id\":" + i + ",\"handle\":\"p" + i + "\",\"name\":\"P\",\"price_in_cents\":100}}")) + "]";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, page));

        var listing = await Gateway(handler).ListPlansAsync(default);

        Assert.False(listing.IsComplete);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task ValidationRejectionOnCreateSubscriptionIsRejectedNotUnknown()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["bad product"]}"""));

        var ex = await Assert.ThrowsAsync<MaxioGatewayException>(
            () => Gateway(handler).CreateSubscriptionAsync(1, "x", "ref", default));

        Assert.Equal(MaxioFailureKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.HttpStatus);
        Assert.Contains("bad product", ex.Message);
    }

    [Fact]
    public async Task ConnectionFailureOnCreateSubscriptionIsAnUnknownOutcomeAndIsSentOnce()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<MaxioGatewayException>(
            () => Gateway(handler).CreateSubscriptionAsync(1, "x", "ref", default));

        Assert.Equal(MaxioFailureKind.UnknownOutcome, ex.Kind);
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ConnectionFailureOnCreateCustomerIsAnUnknownOutcome()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<MaxioGatewayException>(
            () => Gateway(handler).CreateCustomerAsync("ref", "a", "b", "a@b.test", default));

        Assert.Equal(MaxioFailureKind.UnknownOutcome, ex.Kind);
    }

    [Fact]
    public async Task UnreadableSuccessBodyOnWriteIsAnUnknownOutcome()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"subscription":"not-an-object"}"""));

        var ex = await Assert.ThrowsAsync<MaxioGatewayException>(
            () => Gateway(handler).CreateSubscriptionAsync(1, "x", "ref", default));

        Assert.Equal(MaxioFailureKind.UnknownOutcome, ex.Kind);
    }

    [Fact]
    public async Task UnauthorizedIsOurFaultAndUnavailable()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<MaxioGatewayException>(
            () => Gateway(handler).CreateSubscriptionAsync(1, "x", "ref", default));

        Assert.Equal(MaxioFailureKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task MissingCustomerReferenceIsNullNotAnError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });

        Assert.Null(await Gateway(handler).FindCustomerIdByReferenceAsync("nope", default));
    }

    [Fact]
    public async Task MissingSubscriptionReferenceIsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });

        Assert.Null(await Gateway(handler).FindSubscriptionByReferenceAsync("nope", default));
    }
}
