using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using NSubstitute;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class PayPalPaymentProcessorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, (HttpStatusCode, string)> _responder;
        public List<(string Path, string Body)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((path, body));
            var (status, json) = _responder(request, body);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }

    private const string TokenJson = "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}";

    private static PayPalPaymentProcessor Build(StubHandler handler) =>
        new(new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        }), Substitute.For<IAppLogger<PayPalPaymentProcessor>>());

    private static AuthorizationRequest Request() => new(
        OrderId: 1, Amount: 51m, CurrencyCode: "USD",
        ReconciliationReference: "ESHOP-1", PaymentReference: "REF-1",
        Instrument: new PaymentInstrument(new CardDetails("4111111111111111", "2028-12", "123", "Demo"), null, null));

    [Fact]
    public async Task Authorize_success_mapsAuthorizationAndCardDescription_andSendsCustomAndInvoiceIds()
    {
        const string orderJson = """
        {
          "id": "PP-ORDER-1",
          "status": "COMPLETED",
          "payment_source": { "card": { "last_digits": "1111", "brand": "VISA" } },
          "purchase_units": [
            { "payments": { "authorizations": [
                { "id": "AUTH-XYZ", "status": "CREATED",
                  "amount": { "currency_code": "USD", "value": "51.00" },
                  "expiration_time": "2028-12-31T00:00:00Z" } ] } }
          ]
        }
        """;

        var handler = new StubHandler((req, _) =>
            req.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? (HttpStatusCode.OK, TokenJson)
                : (HttpStatusCode.Created, orderJson));

        var processor = Build(handler);

        var result = await processor.AuthorizeAsync(Request(), CancellationToken.None);

        Assert.Equal(AuthorizationOutcome.Authorized, result.Outcome);
        Assert.Equal("PP-ORDER-1", result.ProcessorOrderId);
        Assert.Equal("AUTH-XYZ", result.AuthorizationId);
        Assert.Equal("VISA ending 1111", result.PaymentMethodDescription);

        // The outgoing create-order body carries the stable custom_id and the unique invoice_id.
        var create = handler.Requests.Find(r => r.Path.EndsWith("/v2/checkout/orders"));
        Assert.Contains("\"custom_id\":\"ESHOP-1\"", create.Body);
        Assert.Contains("\"invoice_id\":\"REF-1\"", create.Body);
        Assert.Contains("\"intent\":\"AUTHORIZE\"", create.Body);
    }

    [Fact]
    public async Task Authorize_whenPayPalRejects_throwsPaymentGatewayException()
    {
        const string errorJson = """
        { "name": "UNPROCESSABLE_ENTITY", "message": "bad", "debug_id": "abc123",
          "details": [ { "issue": "INSTRUMENT_DECLINED", "description": "declined" } ] }
        """;

        var handler = new StubHandler((req, _) =>
            req.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? (HttpStatusCode.OK, TokenJson)
                : (HttpStatusCode.UnprocessableEntity, errorJson));

        var processor = Build(handler);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            processor.AuthorizeAsync(Request(), CancellationToken.None));
        Assert.Equal(PaymentGatewayFailureKind.RequestRejected, ex.Kind);
    }
}
