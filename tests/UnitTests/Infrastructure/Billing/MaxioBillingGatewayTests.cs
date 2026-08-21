using System.Net;
using System.Text;
using System.Text.Json;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListsPlansUsingConfiguredFamilyHandleAndRuntimeFamilyId()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/product_families.json")
            {
                return Json(HttpStatusCode.OK,
                    """[{"product_family":{"id":42,"name":"Portable","handle":"portable-family"}}]""");
            }

            if (request.RequestUri.AbsolutePath == "/product_families/42/products.json")
            {
                return Json(HttpStatusCode.OK,
                    """[{"product":{"id":71,"name":"Portable Plan","handle":"portable-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","product_price_point_handle":"default"}}]""");
            }

            return Json(HttpStatusCode.NotFound, "{}" );
        });
        var gateway = CreateGateway(handler, "portable-family");

        var plans = await gateway.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("portable-plan", plan.ProductHandle);
        Assert.Equal("default", plan.PricePointHandle);
        Assert.Equal(2900, plan.PriceInCents);
        Assert.Contains(handler.Requests, request =>
            request.RequestUri!.AbsolutePath == "/product_families/42/products.json");
    }

    [Fact]
    public async Task FindsNoCustomerOnNotFoundResponse()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var gateway = CreateGateway(handler, "portable-family");

        var customer = await gateway.FindCustomerAsync("customer-reference", default);

        Assert.Null(customer);
    }

    [Theory]
    [InlineData(true, "remittance")]
    [InlineData(false, "invoice")]
    public async Task CreatesSubscriptionWithoutStoredPaymentMethod(
        bool relationshipInvoicingEnabled,
        string expectedCollectionMethod)
    {
        string? requestJson = null;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/site.json")
            {
                return Json(HttpStatusCode.OK,
                    $"{{\"site\":{{\"relationship_invoicing_enabled\":{relationshipInvoicingEnabled.ToString().ToLowerInvariant()}}}}}");
            }

            requestJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK,
                """{"subscription":{"id":456,"reference":"eshop-sub","customer":{"id":123,"reference":"eshop-user"},"product":{"handle":"eshop-pro","name":"Pro"}}}""");
        });
        var gateway = CreateGateway(handler, "portable-family");

        var subscription = await gateway.CreateSubscriptionAsync(
            new(
                ProductHandle: "eshop-pro",
                PricePointHandle: null,
                CustomerReference: "eshop-user",
                Reference: "eshop-sub"),
            default);

        Assert.Equal(456, subscription.Id);
        Assert.NotNull(requestJson);
        using var json = JsonDocument.Parse(requestJson);
        var body = json.RootElement.GetProperty("subscription");
        Assert.Equal(expectedCollectionMethod, body.GetProperty("payment_collection_method").GetString());
        Assert.False(body.TryGetProperty("product_price_point_handle", out _));
    }

    private static MaxioBillingGateway CreateGateway(StubHandler handler, string familyHandle)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(1)
            }
        };
        clientOptions.Server.Production.Us.Site = "test";
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = familyHandle,
            BaseUrl = "https://maxio.test"
        });
        return new MaxioBillingGateway(
            client,
            options,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioBillingGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
