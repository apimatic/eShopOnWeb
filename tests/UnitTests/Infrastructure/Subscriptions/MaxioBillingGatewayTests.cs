using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListsOnlyConfiguredFamilyAndUsesHandleSafeCatalogCall()
    {
        const string body = """
            [
              {"product":{"name":"Pro","handle":"eshop-pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"handle":"eshop-subscribe"}}},
              {"product":{"name":"Other","handle":"other","price_in_cents":100,"require_credit_card":false,"product_family":{"handle":"another-family"}}}
            ]
            """;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var gateway = CreateGateway(handler);

        var plans = await gateway.GetPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/products.json", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("page=1", handler.LastRequest.RequestUri.Query);
        Assert.Contains("per_page=20", handler.LastRequest.RequestUri.Query);
        Assert.Contains("include_archived=false", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task CreatesSubscriptionWithStableReferencesAndNoNumericOrPaymentFields()
    {
        const string responseBody = """
            {"subscription":{"reference":"eshop-subscription-abc","state":"active","product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-09-21T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro"}}}
            """;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.Created, responseBody));
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateSubscriptionAsync(
            "eshop-pro",
            "eshop-customer-abc",
            "eshop-subscription-abc",
            default);

        Assert.Equal("active", result.State);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/subscriptions.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var requestBody = handler.LastRequestBody!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", requestBody);
        Assert.Contains("\"customer_reference\":\"eshop-customer-abc\"", requestBody);
        Assert.Contains("\"reference\":\"eshop-subscription-abc\"", requestBody);
        Assert.DoesNotContain("_id\"", requestBody);
        Assert.DoesNotContain("payment", requestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credit_card", requestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bank_account", requestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSubscriptionSurfacesSafeTypedValidationErrors()
    {
        const string responseBody = """
            {"errors":["Reference has already been taken.","API key: must never be returned"]}
            """;
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.UnprocessableEntity, responseBody));
        var gateway = CreateGateway(handler);

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            gateway.CreateSubscriptionAsync(
                "eshop-pro",
                "eshop-customer-abc",
                "eshop-subscription-abc",
                default));

        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Contains("Reference has already been taken.", exception.Message);
        Assert.DoesNotContain("API key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must never be returned", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MaxioBillingGateway CreateGateway(StubHandler handler)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = "unit-test",
                Password = "unit-test"
            }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);
        return new MaxioBillingGateway(client, new MaxioOptions
        {
            ApiKey = "unit-test",
            Subdomain = "unit-test",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://maxio.test"
        });
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response(request);
        }
    }
}
