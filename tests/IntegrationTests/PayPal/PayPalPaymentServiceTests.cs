using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using PayPalServerSdk;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Tests the SDK seam (the HttpClient the client is built from) so the wiring, response mapping and error
/// translation are exercised without a network call. Auth is left unset so no token round-trip is needed.
/// </summary>
public class PayPalPaymentServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static PayPalPaymentService ServiceWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new PayPalServerSdkClient(new HttpClient(new StubHandler(responder)),
            new PayPalServerSdkClientOptions());
        return new PayPalPaymentService(client);
    }

    private static readonly PaymentSourceInput Card =
        new(new CardDetails("4111111111111111", "2030-01", "123", "N", "US"), null);

    [Fact]
    public async Task Authorize_ParsesOrderAndAuthorizationIds()
    {
        var svc = ServiceWith(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/authorize"))
            {
                return Json(HttpStatusCode.Created, """
                {"id":"ORDER1","status":"COMPLETED","purchase_units":[
                  {"payments":{"authorizations":[
                    {"id":"AUTH1","status":"CREATED","expiration_time":"2030-01-01T00:00:00Z"}]}}]}
                """);
            }
            return Json(HttpStatusCode.Created, """{"id":"ORDER1","status":"CREATED"}""");
        });

        var result = await svc.AuthorizeAsync(12.34m, "USD", "INV-1", "1", Card, "idem-1");

        Assert.Equal("ORDER1", result.PayPalOrderId);
        Assert.Equal("AUTH1", result.AuthorizationId);
        Assert.Equal("CREATED", result.AuthorizationStatus);
        Assert.False(result.RequiresBuyerAction);
    }

    [Fact]
    public async Task Authorize_PayerActionRequired_ReportsStop()
    {
        var svc = ServiceWith(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/authorize"))
            {
                return Json(HttpStatusCode.OK, """
                {"id":"ORDER1","status":"PAYER_ACTION_REQUIRED",
                 "links":[{"href":"https://paypal/approve","rel":"payer-action","method":"GET"}]}
                """);
            }
            return Json(HttpStatusCode.Created, """{"id":"ORDER1","status":"CREATED"}""");
        });

        var ex = await Assert.ThrowsAsync<PayPalBuyerActionRequiredException>(() =>
            svc.AuthorizeAsync(10m, "USD", "INV-1", "1", Card, "idem-2"));
        Assert.Equal("payer-action", ex.ActionRel);
        Assert.Equal("https://paypal/approve", ex.ActionHref);
    }

    [Fact]
    public async Task BaseUrlOverride_RoutesEveryCallToTheCustomHost()
    {
        var hosts = new List<string>();
        var options = new PayPalServerSdkClientOptions
        {
            Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = "https://mock.paypal.local" }
                }
            }
        };
        var handler = new StubHandler(req =>
        {
            hosts.Add(req.RequestUri!.Host);
            if (req.RequestUri!.AbsolutePath.EndsWith("/authorize"))
            {
                return Json(HttpStatusCode.Created,
                    """{"id":"ORDER1","status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[{"id":"AUTH1","status":"CREATED"}]}}]}""");
            }
            return Json(HttpStatusCode.Created, """{"id":"ORDER1","status":"CREATED"}""");
        });
        var svc = new PayPalPaymentService(new PayPalServerSdkClient(new HttpClient(handler), options));

        await svc.AuthorizeAsync(1m, "USD", "INV-1", "1", Card, "idem-base");

        Assert.NotEmpty(hosts);
        Assert.All(hosts, h => Assert.Equal("mock.paypal.local", h));
    }

    [Fact]
    public async Task Authorize_TypedError_TranslatesToProvider4xx()
    {
        var svc = ServiceWith(_ => Json(HttpStatusCode.UnprocessableEntity, """
            {"name":"UNPROCESSABLE_ENTITY","message":"The requested action could not be performed.",
             "debug_id":"dbg1","details":[{"issue":"CARD_EXPIRED","description":"The card is expired."}]}
            """));

        var ex = await Assert.ThrowsAsync<PayPalPaymentException>(() =>
            svc.AuthorizeAsync(10m, "USD", "INV-1", "1", Card, "idem-3"));

        Assert.Equal(400, ex.ProviderStatusCode);
        Assert.Contains("CARD_EXPIRED", ex.Message);
    }
}
