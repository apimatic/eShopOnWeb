using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class PayPalPaymentGatewayTests
{
    [Fact]
    public async Task UsesSpecPathsMoneyShapeSavedCardAndIdempotencyHeaders()
    {
        var calls = new List<(HttpRequestMessage Request, string Body)>();
        var handler = new StubHandler(async (request, index) =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            calls.Add((CloneWithoutContent(request), body));
            return index switch
            {
                0 => Json(HttpStatusCode.OK, """{"access_token":"access","expires_in":3600}"""),
                1 => Json(HttpStatusCode.Created, """{"id":"ORDER123","status":"CREATED"}"""),
                2 => Json(HttpStatusCode.Created, """
                {
                  "id":"ORDER123","status":"COMPLETED",
                  "payment_source":{"card":{"brand":"VISA","last_digits":"1111"}},
                  "purchase_units":[{"payments":{"authorizations":[{
                    "id":"AUTH123","status":"CREATED","amount":{"currency_code":"USD","value":"19.50"},
                    "create_time":"2026-08-31T01:00:00Z","expiration_time":"2026-09-29T01:00:00Z"
                  }]}}]
                }
                """),
                _ => throw new InvalidOperationException()
            };
        });
        var gateway = CreateGateway(handler);

        var order = await gateway.CreateOrderAsync(19.5m, "USD", "ESHOP-INVOICE", "eshop-order-ref",
            "create-key", default);
        var authorization = await gateway.AuthorizeOrderAsync(order.Id, PayPalPaymentSource.Saved("VAULT123"),
            "authorize-key", default);

        Assert.Equal("/v1/oauth2/token", calls[0].Request.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", calls[0].Request.Headers.Authorization!.Scheme);
        Assert.Equal("grant_type=client_credentials", calls[0].Body);
        Assert.Equal("/v2/checkout/orders", calls[1].Request.RequestUri!.AbsolutePath);
        Assert.Equal("create-key", calls[1].Request.Headers.GetValues("PayPal-Request-Id").Single());
        using (var create = JsonDocument.Parse(calls[1].Body))
        {
            Assert.Equal("AUTHORIZE", create.RootElement.GetProperty("intent").GetString());
            var purchaseUnit = create.RootElement.GetProperty("purchase_units")[0];
            Assert.Equal("19.50", purchaseUnit.GetProperty("amount").GetProperty("value").GetString());
            Assert.Equal("USD", purchaseUnit.GetProperty("amount").GetProperty("currency_code").GetString());
            Assert.Equal("ESHOP-INVOICE", purchaseUnit.GetProperty("invoice_id").GetString());
        }
        Assert.Equal("/v2/checkout/orders/ORDER123/authorize", calls[2].Request.RequestUri!.AbsolutePath);
        Assert.Equal("authorize-key", calls[2].Request.Headers.GetValues("PayPal-Request-Id").Single());
        using (var authorize = JsonDocument.Parse(calls[2].Body))
        {
            var card = authorize.RootElement.GetProperty("payment_source").GetProperty("card");
            Assert.Equal("VAULT123", card.GetProperty("vault_id").GetString());
            Assert.Equal("CUSTOMER", card.GetProperty("stored_credential")
                .GetProperty("payment_initiator").GetString());
            Assert.False(card.TryGetProperty("number", out _));
        }
        Assert.Equal("AUTH123", authorization.Id);
        Assert.Equal(19.5m, authorization.Amount);
    }

    [Fact]
    public async Task VaultsThroughSetupTokenThenPaymentTokenAndReturnsOnlySafeCardData()
    {
        var bodies = new List<string>();
        var paths = new List<string>();
        var handler = new StubHandler(async (request, index) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            return index switch
            {
                0 => Json(HttpStatusCode.OK, """{"access_token":"access","expires_in":3600}"""),
                1 => Json(HttpStatusCode.Created, """{"id":"SETUP123","status":"APPROVED"}"""),
                2 => Json(HttpStatusCode.Created, """
                    {"id":"TOKEN123","customer":{"id":"CUSTOMER123"},"payment_source":{"card":{
                    "brand":"VISA","last_digits":"1111","expiry":"2028-12"}}}
                    """),
                _ => throw new InvalidOperationException()
            };
        });
        var gateway = CreateGateway(handler);
        var card = new PayPalCard("4111111111111111", "2028-12", "123", "Sandbox Shopper",
            new PayPalAddress("123 Main", null, "San Jose", "CA", "95131", "US"));

        var result = await gateway.VaultCardAsync(card, "eshop_merchant_customer", "setup-key", "token-key",
            default);

        Assert.Equal("/v3/vault/setup-tokens", paths[1]);
        Assert.Contains("4111111111111111", bodies[1]);
        Assert.Equal("/v3/vault/payment-tokens", paths[2]);
        using var tokenRequest = JsonDocument.Parse(bodies[2]);
        var token = tokenRequest.RootElement.GetProperty("payment_source").GetProperty("token");
        Assert.Equal("SETUP123", token.GetProperty("id").GetString());
        Assert.Equal("SETUP_TOKEN", token.GetProperty("type").GetString());
        Assert.Equal("TOKEN123", result.Id);
        Assert.Equal("1111", result.Last4);
    }

    private static PayPalPaymentGateway CreateGateway(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new PayPalSettings
        {
            ClientId = "client",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
            BaseUrl = "https://api-m.sandbox.paypal.com"
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage CloneWithoutContent(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        private int _index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request, _index++);
    }
}
