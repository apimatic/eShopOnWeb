using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace PublicApiIntegrationTests.PayPal;

[TestClass]
public class PayPalPaymentServiceTests
{
    private static PayPalPaymentService BuildService(StubHttpMessageHandler stub)
    {
        var httpClient = new HttpClient(new PayPalStatusCaptureHandler { InnerHandler = stub });
        var client = new PayPalServerSdkClient(httpClient, new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        });
        var settings = Options.Create(new PayPalSettings { Currency = "USD", Environment = "sandbox" });
        return new PayPalPaymentService(client, settings, NullLogger<PayPalPaymentService>.Instance);
    }

    private static CardDetails TestCard() =>
        new("4111111111111111", "2027-12", "123", "Tester", new CardBillingAddress(null, null, null, null, null, "US"));

    [TestMethod]
    public async Task Authorize_ReturnsHold_FromCreateResponse()
    {
        // CreateOrder with a card auto-authorizes: the authorization is embedded in the create response.
        const string body = """
        {"id":"ORDER123","status":"COMPLETED","purchase_units":[
          {"payments":{"authorizations":[
            {"id":"AUTH123","status":"CREATED","expiration_time":"2026-09-19T05:00:00Z"}]}}]}
        """;
        var stub = new StubHttpMessageHandler(HttpStatusCode.Created, body);
        var service = BuildService(stub);

        var result = await service.AuthorizeAsync(
            new PaymentAuthorizationRequest("REF-1", 51.00m, TestCard(), null), "auth-1");

        Assert.AreEqual("ORDER123", result.PayPalOrderId);
        Assert.AreEqual("AUTH123", result.AuthorizationId);
        Assert.AreEqual("CREATED", result.Status);
        // Exactly one non-token call: no separate AuthorizeOrder was needed.
        Assert.AreEqual(1, stub.NonTokenRequestCount);
    }

    [TestMethod]
    public async Task Authorize_ProviderRejection_MapsTo422()
    {
        const string body = """
        {"name":"UNPROCESSABLE_ENTITY","message":"failed","debug_id":"abc",
         "details":[{"issue":"INSTRUMENT_DECLINED","description":"declined"}]}
        """;
        var stub = new StubHttpMessageHandler(HttpStatusCode.UnprocessableEntity, body);
        var service = BuildService(stub);

        var ex = await Assert.ThrowsExceptionAsync<PaymentProcessorException>(() =>
            service.AuthorizeAsync(new PaymentAuthorizationRequest("REF-1", 51.00m, TestCard(), null), "auth-1"));

        Assert.AreEqual(422, ex.StatusCode);
        // The caller-safe message must not leak provider internals (debug id / raw body).
        StringAssert.Contains(ex.Message, "could not be processed");
        Assert.IsFalse(ex.Message.Contains("debug_id"));
    }

    [TestMethod]
    public async Task Capture_ParsesFeeAndNetProceeds()
    {
        const string body = """
        {"id":"CAP-1","status":"COMPLETED","amount":{"currency_code":"USD","value":"51.00"},
         "seller_receivable_breakdown":{
           "gross_amount":{"currency_code":"USD","value":"51.00"},
           "paypal_fee":{"currency_code":"USD","value":"1.81"},
           "net_amount":{"currency_code":"USD","value":"49.19"}}}
        """;
        var stub = new StubHttpMessageHandler(HttpStatusCode.Created, body);
        var service = BuildService(stub);

        var result = await service.CaptureAsync("AUTH123", 51.00m, "capture-1");

        Assert.AreEqual("CAP-1", result.CaptureId);
        Assert.AreEqual(51.00m, result.GrossAmount);
        Assert.AreEqual(1.81m, result.PayPalFee);
        Assert.AreEqual(49.19m, result.NetAmount);
    }

    [TestMethod]
    public async Task Void_204NoContent_IsTreatedAsSuccess()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
        var service = BuildService(stub);

        // Should not throw: a 204 with an empty body means the hold was released.
        await service.VoidAsync("AUTH123", "void-1");

        Assert.AreEqual(1, stub.NonTokenRequestCount);
    }

    [TestMethod]
    public async Task Refund_ParsesRefundId()
    {
        const string body = """
        {"id":"RF-1","status":"COMPLETED","amount":{"currency_code":"USD","value":"10.00"}}
        """;
        var stub = new StubHttpMessageHandler(HttpStatusCode.Created, body);
        var service = BuildService(stub);

        var result = await service.RefundAsync("CAP-1", 10.00m, "refund-key");

        Assert.AreEqual("RF-1", result.RefundId);
        Assert.AreEqual(10.00m, result.Amount);
        Assert.AreEqual("COMPLETED", result.Status);
    }
}
