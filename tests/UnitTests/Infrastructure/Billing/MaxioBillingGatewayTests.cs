using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListsPlansFromConfiguredFamilyHandle()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return JsonResponse("""
                [{"product":{"id":7,"name":"Pro","handle":"pro","description":"Plan",
                "price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,
                "product_family":{"handle":"family"}}}]
                """);
        });
        var gateway = CreateGateway(handler);

        var plans = await gateway.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Contains("product_families/handle%3Afamily/products.json", captured!.RequestUri!.OriginalString);
        Assert.Equal("Basic", captured.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task CreatesSubscriptionUsingVerifiedMaxioShape()
    {
        string? requestJson = null;
        var handler = new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {"subscription":{"id":22,"state":"active","reference":"app-ref",
                "product_price_in_cents":29900,"next_assessment_at":"2026-09-20T12:00:00Z",
                "customer":{"id":11},"product":{"id":7,"name":"Pro","handle":"pro",
                "price_in_cents":29900,"interval":1,"interval_unit":"month",
                "product_family":{"handle":"family"}}}}
                """, HttpStatusCode.Created);
        });
        var gateway = CreateGateway(handler);

        var subscription = await gateway.CreateSubscriptionAsync(11, "pro", "app-ref");

        using var json = JsonDocument.Parse(requestJson!);
        var body = json.RootElement.GetProperty("subscription");
        Assert.Equal("pro", body.GetProperty("product_handle").GetString());
        Assert.Equal(11, body.GetProperty("customer_id").GetInt64());
        Assert.Equal("app-ref", body.GetProperty("reference").GetString());
        Assert.Equal("remittance", body.GetProperty("payment_collection_method").GetString());
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.NotNull(subscription.NextBillingAt);
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "fake-api-key",
            Subdomain = "fake",
            ProductFamilyHandle = "family",
            BaseUrl = "https://maxio.example.test"
        });
        return new MaxioBillingGateway(new HttpClient(handler), options, new MaxioConcurrencyLimiter());
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = request => Task.FromResult(handler(request));

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
