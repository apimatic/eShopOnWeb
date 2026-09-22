using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Exercises the gateway against a faked HttpClient (the SDK's constructor seam), asserting the real
/// mapping/translation behaviour rather than reaching PayPal.
/// </summary>
[TestClass]
public class PayPalGatewayTests
{
    private sealed class NoopLogger : IAppLogger<PayPalGateway>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _operationResponder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> operationResponder)
            => _operationResponder = operationResponder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path.Contains("/v1/oauth2/token"))
            {
                response = Json(HttpStatusCode.OK,
                    "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":32400}");
            }
            else
            {
                response = _operationResponder(request);
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static PayPalGateway CreateGateway(Func<HttpRequestMessage, HttpResponseMessage> operationResponder)
    {
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "test-id", ClientSecret = "test-secret" },
        };
        var client = new PayPalServerSdkClient(new HttpClient(new StubHandler(operationResponder)), options);
        var payPalOptions = Options.Create(new PayPalOptions
        {
            ClientId = "test-id",
            ClientSecret = "test-secret",
            Environment = "Sandbox",
            Currency = "USD",
        });
        return new PayPalGateway(client, payPalOptions, new NoopLogger());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [TestMethod]
    public async Task Capture_MapsSellerReceivableBreakdownToFeeAndNet()
    {
        const string captureBody = """
        {
          "id": "CAP123",
          "status": "COMPLETED",
          "amount": { "currency_code": "USD", "value": "29.00" },
          "seller_receivable_breakdown": {
            "gross_amount": { "currency_code": "USD", "value": "29.00" },
            "paypal_fee": { "currency_code": "USD", "value": "1.24" },
            "net_amount": { "currency_code": "USD", "value": "27.76" }
          }
        }
        """;
        var gateway = CreateGateway(_ => Json(HttpStatusCode.Created, captureBody));

        var result = await gateway.CaptureAsync("ref-1", "AUTH1", new GatewayMoney("USD", "29.00"), CancellationToken.None);

        Assert.AreEqual("CAP123", result.CaptureId);
        Assert.AreEqual("COMPLETED", result.Status);
        Assert.AreEqual("29.00", result.GrossAmount?.Value);
        Assert.AreEqual("1.24", result.PaypalFee?.Value);
        Assert.AreEqual("27.76", result.NetAmount?.Value);
    }

    [TestMethod]
    public async Task Capture_TranslatesTypedErrorAndFlagsExpiredAuthorization()
    {
        const string errorBody = """
        {
          "name": "UNPROCESSABLE_ENTITY",
          "message": "The requested action could not be performed.",
          "debug_id": "debug-123",
          "details": [ { "issue": "AUTHORIZATION_EXPIRED", "description": "The authorization has expired." } ]
        }
        """;
        var gateway = CreateGateway(_ => Json((HttpStatusCode)422, errorBody));

        var ex = await Assert.ThrowsExceptionAsync<PayPalGatewayException>(
            () => gateway.CaptureAsync("ref-1", "AUTH1", new GatewayMoney("USD", "29.00"), CancellationToken.None));

        Assert.AreEqual("AUTHORIZATION_EXPIRED", ex.Issue);
        Assert.IsTrue(ex.IsAuthorizationExpired);
        Assert.AreEqual("debug-123", ex.DebugId);
    }

    [TestMethod]
    public async Task Void_TreatsEmpty204AsSuccess()
    {
        var gateway = CreateGateway(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // Must not throw: an empty 204 body means the void was accepted.
        await gateway.VoidAsync("ref-1", "AUTH1", CancellationToken.None);
    }
}
