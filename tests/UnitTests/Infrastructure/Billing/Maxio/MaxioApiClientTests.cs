using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioApiClientTests
{
    [Fact]
    public async Task DerivesTheApiHostFromTheSubdomain()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));
        var client = CreateClient(handler, new MaxioOptions { Subdomain = "cp-exp-3" });

        await client.GetSiteAsync();

        Assert.Equal("https://cp-exp-3.chargify.com/site.json", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task UsesTheEuropeanHostWhenTheSiteIsHostedInTheEu()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"EUR"}}"""));
        var client = CreateClient(handler, new MaxioOptions
        {
            Subdomain = "cp-exp-3",
            Environment = MaxioEnvironment.EU
        });

        await client.GetSiteAsync();

        Assert.Equal("https://cp-exp-3.ebilling.maxio.com/site.json", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task UsesAConfiguredBaseUrlVerbatimIncludingItsPathPrefix()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));
        var client = CreateClient(handler, new MaxioOptions
        {
            Subdomain = "ignored-when-base-url-is-set",
            BaseUrl = "https://billing.internal.example.com/maxio"
        });

        await client.GetSiteAsync();

        Assert.Equal("https://billing.internal.example.com/maxio/site.json", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task AuthenticatesWithTheApiKeyAsTheBasicAuthUserName()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}"""));
        var client = CreateClient(handler, new MaxioOptions { ApiKey = "secret-key", Subdomain = "cp-exp-3" });

        await client.GetSiteAsync();

        var header = handler.AuthorizationHeaders[0];
        Assert.StartsWith("Basic ", header);
        Assert.Equal("secret-key:x", Encoding.ASCII.GetString(Convert.FromBase64String(header["Basic ".Length..])));
    }

    [Fact]
    public async Task ReturnsNullWhenACustomerReferenceIsUnknown()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync("nobody");

        Assert.Null(customer);
        Assert.Contains("reference=nobody", handler.Requests[0].Query);
    }

    [Fact]
    public async Task ReadsACustomerLookupHit()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{"customer":{"id":98838743,"reference":"eshoponweb-demo","email":"demo@example.com"}}"""));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync("eshoponweb-demo");

        Assert.NotNull(customer);
        Assert.Equal(98838743, customer!.Id);
        Assert.Equal("demo@example.com", customer.Email);
    }

    [Fact]
    public async Task RecognisesADuplicateReferenceRejection()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""",
            requestId: "req-123"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerReference = "eshoponweb-demo",
                Reference = "eshoponweb-demo--eshop-pro"
            }));

        Assert.True(exception.IsDuplicateReference);
        Assert.Equal("req-123", exception.RequestId);
    }

    [Fact]
    public async Task FlattensErrorsThatAreKeyedByField()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":{"email":["is invalid","is too long"]}}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer { Email = "nope" }));

        Assert.Equal(new[] { "email: is invalid", "email: is too long" }, exception.Errors);
    }

    [Fact]
    public async Task ReportsAnUnreachableApiAsAnAvailabilityFailure()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route to host"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<SubscriptionBillingUnavailableException>(() => client.GetSiteAsync());
    }

    [Fact]
    public async Task ReadsEveryPageOfAListEndpoint()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 200)
            .Select(i => $"{{\"product\":{{\"id\":{i},\"handle\":\"plan-{i}\",\"price_in_cents\":100}}}}")) + "]";

        var handler = new StubHandler(request => request.RequestUri!.Query.Contains("page=1")
            ? Json(HttpStatusCode.OK, fullPage)
            : Json(HttpStatusCode.OK, """[{"product":{"id":201,"handle":"plan-201","price_in_cents":100}}]"""));
        var client = CreateClient(handler);

        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("per_page=200", handler.Requests[0].Query);
        Assert.Equal(
            "/product_families/handle:eshop-subscribe/products.json",
            handler.Requests[0].AbsolutePath);
    }

    private static MaxioApiClient CreateClient(StubHandler handler, MaxioOptions? options = null)
    {
        options ??= new MaxioOptions { ApiKey = "test-key", Subdomain = "cp-exp-3" };

        var httpClient = new HttpClient(handler);
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            httpClient.DefaultRequestHeaders.Authorization = new("Basic", credentials);
        }

        return new MaxioApiClient(httpClient, Options.Create(options), NullLogger<MaxioApiClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body, string? requestId = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (requestId is not null)
        {
            response.Headers.Add("X-Request-Id", requestId);
        }

        return response;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<Uri> Requests { get; } = new();

        public List<string> AuthorizationHeaders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);

            return Task.FromResult(_respond(request));
        }
    }
}
