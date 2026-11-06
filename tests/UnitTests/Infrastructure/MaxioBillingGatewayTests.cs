using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioBillingGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public System.Collections.Generic.List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(System.Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MaxioBillingGateway Gateway(StubHandler handler, string? baseUrl = null)
    {
        var settings = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "fam",
            BaseUrl = baseUrl
        };
        return new MaxioBillingGateway(
            new System.Lazy<MaxioAdvancedBilling.MaxioAdvancedBillingClient>(
                () => MaxioClientFactory.Create(settings, new HttpClient(handler))),
            settings.ProductFamilyHandle);
    }

    [Fact]
    public async Task ListPlans_UsesFamilyHandle_AndMapsPrice()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""));

        var plans = await Gateway(handler).ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(299m, plans[0].Price);
        Assert.Contains("handle:fam", System.Uri.UnescapeDataString(handler.Requests[0].RequestUri!.AbsoluteUri));
        Assert.Equal("test-site.chargify.com", handler.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task BaseUrl_IsUsedVerbatim_WhenSet()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));

        await Gateway(handler, "http://localhost:9").ListPlansAsync();

        Assert.Equal("localhost", handler.Requests[0].RequestUri!.Host);
        Assert.Equal(9, handler.Requests[0].RequestUri!.Port);
    }

    [Fact]
    public async Task FindCustomer_NotFound_ReturnsNull()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));

        Assert.Null(await Gateway(handler).FindCustomerIdAsync("u@x.com"));
    }

    [Fact]
    public async Task FindCustomer_UnreadableBody_IsNotTreatedAsAbsent()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "<html>oops</html>"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => Gateway(handler).FindCustomerIdAsync("u@x.com"));

        Assert.Equal(BillingFailureKind.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task CreateSubscription_422_IsValidationWithMessage()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["bad plan"]}"""));

        var ex = await Assert.ThrowsAsync<BillingException>(() => Gateway(handler).CreateSubscriptionAsync(1, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Validation, ex.Kind);
        Assert.Contains("bad plan", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_IsProviderError_NotLeakingDetails()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"secret":"x"}"""));

        var ex = await Assert.ThrowsAsync<BillingException>(() => Gateway(handler).ListPlansAsync());

        Assert.Equal(BillingFailureKind.ProviderError, ex.Kind);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task ConnectionFailure_IsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("reset"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => Gateway(handler).ListSubscriptionsAsync(1));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task MissingApiKey_IsUnavailable()
    {
        var gateway = new MaxioBillingGateway(
            new System.Lazy<MaxioAdvancedBilling.MaxioAdvancedBillingClient>(
                () => MaxioClientFactory.Create(new MaxioOptions { Subdomain = "s" }, new HttpClient())),
            "fam");

        var ex = await Assert.ThrowsAsync<BillingException>(() => gateway.ListPlansAsync());

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }
}
