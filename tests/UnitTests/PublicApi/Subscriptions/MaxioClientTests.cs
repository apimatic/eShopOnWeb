using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class MaxioClientTests
{
    [Fact]
    public async Task CreateSubscriptionUsesVerifiedMaxioContract()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new DelegateHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.Created, SubscriptionJson);
        });
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(
            new MaxioSubscriptionDraft(42, "test-plan", "test-reference"),
            "2731fb23-98ad-4489-baf6-7d5ce916f766", CancellationToken.None);

        Assert.Equal(9001, result.Id);
        Assert.Equal(new Uri("https://maxio.example.test/root/subscriptions.json"), capturedRequest!.RequestUri);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("api-key:x", DecodeBasicCredentials(capturedRequest.Headers.Authorization));

        using var json = JsonDocument.Parse(capturedBody!);
        var root = json.RootElement;
        Assert.Equal("2731fb23-98ad-4489-baf6-7d5ce916f766",
            root.GetProperty("uniqueness_token").GetString());
        var subscription = root.GetProperty("subscription");
        Assert.Equal("test-plan", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(42, subscription.GetProperty("customer_id").GetInt64());
        Assert.Equal("test-reference", subscription.GetProperty("reference").GetString());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task LookupReturnsNullForNotFound()
    {
        var handler = new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerAsync("user id", CancellationToken.None);

        Assert.Null(customer);
        Assert.Equal("https://maxio.example.test/root/customers/lookup.json?reference=user%20id",
            handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public void OptionsValidatorRejectsInsecureBaseUrl()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "family",
            BaseUrl = "http://maxio.example.test"
        });

        Assert.False(result.Succeeded);
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "api-key",
            Subdomain = "ignored-by-override",
            ProductFamilyHandle = "test-family",
            BaseUrl = "https://maxio.example.test/root"
        });
        return new MaxioClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string DecodeBasicCredentials(AuthenticationHeaderValue? authorization)
    {
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        return Encoding.ASCII.GetString(Convert.FromBase64String(authorization.Parameter!));
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return _handler(request);
        }
    }

    private const string SubscriptionJson = """
        {
          "subscription": {
            "id": 9001,
            "state": "active",
            "reference": "test-reference",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-21T12:00:00-04:00",
            "customer": {
              "id": 42,
              "first_name": "Demo",
              "last_name": "Customer",
              "email": "demo@example.com",
              "reference": "user-1"
            },
            "product": {
              "id": 7126957,
              "name": "Pro Plan",
              "handle": "test-plan",
              "description": "Pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "archived_at": null,
              "product_family": {
                "id": 3023074,
                "name": "eShop Subscribe",
                "handle": "test-family"
              }
            }
          }
        }
        """;
}
