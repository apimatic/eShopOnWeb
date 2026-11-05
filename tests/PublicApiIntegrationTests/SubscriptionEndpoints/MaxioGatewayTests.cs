using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri;
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MaxioGateway Gateway(StubHandler handler, string? baseUrl = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-placeholder",
            Subdomain = "test-site",
            ProductFamilyHandle = "fam",
            BaseUrl = baseUrl
        };
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-placeholder", Password = "x" },
        };
        if (baseUrl is not null)
            options.Server.Production.Us.BaseUrl = baseUrl;
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        return new MaxioGateway(client, Options.Create(settings), NullLogger<MaxioGateway>.Instance);
    }

    [TestMethod]
    public async Task ListPlans_DropsArchivedProducts_AndUsesFamilyHandle()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """[{"product":{"id":1,"handle":"a","name":"A","price_in_cents":100}},{"product":{"id":2,"handle":"b","archived_at":"2024-01-01T00:00:00Z"}}]"""));
        var (products, partial) = await Gateway(handler, "http://maxio.test").ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, products.Count);
        Assert.AreEqual("a", products[0].Handle);
        Assert.IsFalse(partial);
        StringAssert.Contains(handler.LastUri!.AbsoluteUri, "handle%3Afam");
        Assert.AreEqual("maxio.test", handler.LastUri.Host);   // BaseUrl override used verbatim
    }

    [TestMethod]
    public async Task FindCustomer_NotFound_ReturnsNull()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        Assert.IsNull(await Gateway(handler).FindCustomerIdByReferenceAsync("r", CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateSubscription_422_IsRejectedWithMaxioMessage()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method"]}"""));
        var ex = await Assert.ThrowsExceptionAsync<MaxioRejectedException>(
            () => Gateway(handler).CreateSubscriptionAsync(1, "p", "ref", CancellationToken.None));
        StringAssert.Contains(ex.Message, "No payment method");
    }

    [TestMethod]
    public async Task CreateSubscription_ConnectionFailure_IsUnknownOutcome_AndSentOnce()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        await Assert.ThrowsExceptionAsync<MaxioOutcomeUnknownException>(
            () => Gateway(handler).CreateSubscriptionAsync(1, "p", "ref", CancellationToken.None));
        Assert.AreEqual(1, handler.Calls);   // the SDK never resends a POST
    }

    [TestMethod]
    public async Task CreateCustomer_ConnectionFailure_IsUnknownOutcome()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        await Assert.ThrowsExceptionAsync<MaxioOutcomeUnknownException>(
            () => Gateway(handler).CreateCustomerAsync("f", "l", "e@x.test", "ref", CancellationToken.None));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task ListCustomerSubscriptions_Unauthorized_IsUnavailableNotCallerError()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        await Assert.ThrowsExceptionAsync<MaxioUnavailableException>(
            () => Gateway(handler).ListCustomerSubscriptionsAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public void Settings_MissingApiKey_FailsValidation()
    {
        var s = new MaxioSettings { Subdomain = "x", ProductFamilyHandle = "f" };
        Assert.IsTrue(s.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(s)).Any());
    }

    [TestMethod]
    public void Settings_BaseUrlWithoutSubdomain_IsValid()
    {
        var s = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "http://mock.test" };
        Assert.IsFalse(s.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(s)).Any());
    }
}
