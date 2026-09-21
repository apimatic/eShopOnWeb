using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PayPalServerSdk;

namespace PublicApiIntegrationTests;

/// <summary>
/// Unit tests for the PayPal gateway using the SDK's HttpClient constructor as the fake seam — no
/// network. They lock in the behaviours a signature does not reveal: capture-breakdown parsing, the
/// 204-No-Content void, and typed-error translation.
/// </summary>
[TestClass]
public class PayPalGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var res = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
            return Task.FromResult(res);
        }
    }

    private static PayPalGateway GatewayReturning(HttpStatusCode status, string body)
    {
        var client = new PayPalServerSdkClient(new HttpClient(new StubHandler(status, body)),
            new PayPalServerSdkClientOptions());
        return new PayPalGateway(client, NullLogger<PayPalGateway>.Instance);
    }

    [TestMethod]
    public async Task CaptureAsync_parses_captured_amount_fee_and_net()
    {
        var json = """
        {"id":"CAP123","status":"COMPLETED","amount":{"currency_code":"USD","value":"47.50"},
         "seller_receivable_breakdown":{"gross_amount":{"currency_code":"USD","value":"47.50"},
         "paypal_fee":{"currency_code":"USD","value":"1.72"},"net_amount":{"currency_code":"USD","value":"45.78"}},
         "create_time":"2026-01-01T00:00:00Z"}
        """;
        var gateway = GatewayReturning(HttpStatusCode.OK, json);

        var result = await gateway.CaptureAsync("AUTH1", "req-1", CancellationToken.None);

        Assert.AreEqual("CAP123", result.CaptureId);
        Assert.AreEqual("COMPLETED", result.Status);
        Assert.AreEqual(47.50m, result.CapturedAmount);
        Assert.AreEqual(1.72m, result.PayPalFee);
        Assert.AreEqual(45.78m, result.NetAmount);
    }

    [TestMethod]
    public async Task VoidAsync_tolerates_204_no_content()
    {
        var gateway = GatewayReturning(HttpStatusCode.NoContent, string.Empty);
        // Must NOT throw on an empty 204 body (that is the successful void).
        await gateway.VoidAsync("AUTH1", "req-void", CancellationToken.None);
    }

    [TestMethod]
    public async Task CaptureAsync_translates_typed_error_to_PayPalException()
    {
        var json = """
        {"name":"UNPROCESSABLE_ENTITY","message":"Business validation failed.","debug_id":"deadbeef123",
         "details":[{"issue":"INSTRUMENT_DECLINED","description":"The card was declined."}]}
        """;
        var gateway = GatewayReturning(HttpStatusCode.UnprocessableEntity, json);

        var ex = await Assert.ThrowsExceptionAsync<PayPalException>(
            () => gateway.CaptureAsync("AUTH1", "req-2", CancellationToken.None));

        Assert.AreEqual("UNPROCESSABLE_ENTITY", ex.ProviderErrorName);
        Assert.AreEqual("deadbeef123", ex.DebugId);
        StringAssert.Contains(ex.Message, "INSTRUMENT_DECLINED");
    }
}
