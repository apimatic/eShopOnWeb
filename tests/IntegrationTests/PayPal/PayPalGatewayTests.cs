using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using NSubstitute;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Tests the PayPal gateway against the SDK's own test seam — an <see cref="HttpClient"/> backed by a
/// stub handler — so no real network calls happen. Verifies the request the SDK builds and the
/// translation of both success and typed/raw error responses.
/// </summary>
public class PayPalGatewayTests
{
    private const string TokenJson = "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public List<string> Bodies { get; } = new();
        public List<Uri?> Uris { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().Result;
            if (request.RequestUri is not null && !request.RequestUri.AbsolutePath.Contains("oauth2/token"))
            {
                Bodies.Add(body);
                Uris.Add(request.RequestUri);
            }
            var response = _responder(request, body);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (PayPalGateway gateway, StubHandler handler) Build(
        Func<HttpRequestMessage, string, HttpResponseMessage> operationResponder)
    {
        var handler = new StubHandler((req, body) =>
            req.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? Json(HttpStatusCode.OK, TokenJson)
                : operationResponder(req, body));

        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        });

        var config = Substitute.For<IPaymentConfiguration>();
        config.Currency.Returns("USD");
        var logger = Substitute.For<IAppLogger<PayPalGateway>>();
        return (new PayPalGateway(client, config, logger), handler);
    }

    [Fact]
    public async Task RefundAsync_FullRefund_SendsNoAmount_AndParsesResult()
    {
        var (gateway, handler) = Build((_, _) =>
            Json(HttpStatusCode.Created, "{\"id\":\"REF1\",\"status\":\"COMPLETED\",\"amount\":{\"currency_code\":\"USD\",\"value\":\"5.00\"}}"));

        var result = await gateway.RefundAsync("CAP1", null, "key-1", CancellationToken.None);

        Assert.Equal("REF1", result.RefundId);
        Assert.Equal(5.00m, result.Amount);
        Assert.DoesNotContain("amount", handler.Bodies[^1]);   // full refund => empty body, no amount
    }

    [Fact]
    public async Task RefundAsync_PartialRefund_IncludesAmount()
    {
        var (gateway, handler) = Build((_, _) =>
            Json(HttpStatusCode.Created, "{\"id\":\"REF2\",\"status\":\"COMPLETED\",\"amount\":{\"currency_code\":\"USD\",\"value\":\"2.50\"}}"));

        var result = await gateway.RefundAsync("CAP1", 2.50m, "key-2", CancellationToken.None);

        Assert.Equal(2.50m, result.Amount);
        Assert.Contains("\"value\":\"2.50\"", handler.Bodies[^1]);
    }

    [Fact]
    public async Task CaptureAsync_ParsesFeeAndNetFromSellerReceivableBreakdown()
    {
        var (gateway, _) = Build((_, _) => Json(HttpStatusCode.Created,
            "{\"id\":\"CAP9\",\"status\":\"COMPLETED\",\"amount\":{\"currency_code\":\"USD\",\"value\":\"39.00\"}," +
            "\"seller_receivable_breakdown\":{\"gross_amount\":{\"currency_code\":\"USD\",\"value\":\"39.00\"}," +
            "\"paypal_fee\":{\"currency_code\":\"USD\",\"value\":\"1.50\"},\"net_amount\":{\"currency_code\":\"USD\",\"value\":\"37.50\"}}}"));

        var result = await gateway.CaptureAsync("AUTH1", 39.00m, "ESHOP-1", "key-cap", CancellationToken.None);

        Assert.Equal("CAP9", result.CaptureId);
        Assert.Equal(39.00m, result.GrossAmount);
        Assert.Equal(1.50m, result.PaypalFee);
        Assert.Equal(37.50m, result.NetAmount);
    }

    [Fact]
    public async Task CaptureAsync_TypedError_TranslatesWithDebugId()
    {
        var (gateway, _) = Build((_, _) => Json((HttpStatusCode)422,
            "{\"name\":\"UNPROCESSABLE_ENTITY\",\"message\":\"failed business validation\",\"debug_id\":\"dbg-123\"," +
            "\"details\":[{\"issue\":\"INSTRUMENT_DECLINED\",\"description\":\"The card was declined.\"}]}"));

        var ex = await Assert.ThrowsAsync<PayPalPaymentException>(
            () => gateway.CaptureAsync("AUTH1", 10m, "ESHOP-1", "key", CancellationToken.None));

        Assert.Equal("dbg-123", ex.DebugId);
        Assert.Contains("INSTRUMENT_DECLINED", ex.Message);
    }

    [Fact]
    public async Task SearchTransactions_RawError_TranslatesWithStatus()
    {
        // Transaction search is the SDK's only Case B (RawError) operation.
        var (gateway, _) = Build((_, _) => Json(HttpStatusCode.BadRequest, "{\"message\":\"bad range\"}"));

        var ex = await Assert.ThrowsAsync<PayPalPaymentException>(
            () => gateway.SearchTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}
