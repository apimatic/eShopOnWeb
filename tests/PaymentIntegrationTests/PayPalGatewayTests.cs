using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.PaymentIntegrationTests;

public class PayPalGatewayTests
{
    private static PayPalGateway BuildGateway(StubHandler handler)
    {
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
        };
        var client = new PayPalServerSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new PayPalSettings { Currency = "USD", Environment = "sandbox" });
        return new PayPalGateway(client, settings, NullLogger<PayPalGateway>.Instance);
    }

    private static bool Ends(HttpRequestMessage r, string suffix)
        => r.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal);

    [Fact]
    public async Task AuthorizeAsync_DirectCard_ReturnsAuthorizationFromTwoStepFlow()
    {
        var handler = new StubHandler(r =>
        {
            if (Ends(r, "/v1/oauth2/token")) return StubHandler.TokenResponse();
            if (r.RequestUri!.AbsolutePath.EndsWith("/v2/checkout/orders", StringComparison.Ordinal))
                return (HttpStatusCode.Created, """{"id":"PPO-123","status":"CREATED"}""");
            if (Ends(r, "/authorize"))
                return (HttpStatusCode.Created, """
                {
                  "id":"PPO-123","status":"COMPLETED",
                  "purchase_units":[{"payments":{"authorizations":[
                    {"id":"AUTH-9","status":"CREATED","expiration_time":"2099-01-01T00:00:00Z",
                     "amount":{"currency_code":"USD","value":"20.00"}}]}}]
                }
                """);
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var gateway = BuildGateway(handler);

        var request = new PayPalAuthorizeRequest("ESHOP-1", "ESHOP-1-abc", "USD", 20m, "order 1", "order-1",
            new PayPalCardDetails("4111111111111111", "2030-01", "123", "Test Buyer", null), null);

        var result = await gateway.AuthorizeAsync(request, default);

        Assert.Equal("PPO-123", result.PayPalOrderId);
        Assert.Equal("AUTH-9", result.AuthorizationId);
        Assert.Equal("CREATED", result.AuthorizationStatus);
    }

    [Fact]
    public async Task AuthorizeAsync_PayerActionRequired_StopsAndReports()
    {
        var handler = new StubHandler(r =>
        {
            if (Ends(r, "/v1/oauth2/token")) return StubHandler.TokenResponse();
            if (r.RequestUri!.AbsolutePath.EndsWith("/v2/checkout/orders", StringComparison.Ordinal))
                return (HttpStatusCode.Created, """{"id":"PPO-1","status":"CREATED"}""");
            if (Ends(r, "/authorize"))
                return (HttpStatusCode.Created, """{"id":"PPO-1","status":"PAYER_ACTION_REQUIRED"}""");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var gateway = BuildGateway(handler);
        var request = new PayPalAuthorizeRequest("ESHOP-1", "ESHOP-1-abc", "USD", 20m, "order 1", "order-1",
            new PayPalCardDetails("4111111111111111", "2030-01", "123", "Test", null), null);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.AuthorizeAsync(request, default));
        Assert.True(ex.OperatorActionable);
    }

    [Fact]
    public async Task CaptureAsync_TypedError_TranslatesStatusAndDebugId()
    {
        var handler = new StubHandler(r =>
        {
            if (Ends(r, "/v1/oauth2/token")) return StubHandler.TokenResponse();
            // capture path: /v2/payments/authorizations/{id}/capture
            return (HttpStatusCode.UnprocessableEntity, """
            {"name":"UNPROCESSABLE_ENTITY","message":"Cannot capture.","debug_id":"dbg-123"}
            """);
        });
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => gateway.CaptureAsync("AUTH-1", "order-1-capture", default));

        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("dbg-123", ex.ProviderDebugId);
    }

    [Fact]
    public async Task CaptureAsync_Success_ReadsSettlementFigures()
    {
        var handler = new StubHandler(r =>
        {
            if (Ends(r, "/v1/oauth2/token")) return StubHandler.TokenResponse();
            return (HttpStatusCode.Created, """
            {
              "id":"CAP-1","status":"COMPLETED",
              "amount":{"currency_code":"USD","value":"20.00"},
              "seller_receivable_breakdown":{
                "gross_amount":{"currency_code":"USD","value":"20.00"},
                "paypal_fee":{"currency_code":"USD","value":"0.88"},
                "net_amount":{"currency_code":"USD","value":"19.12"}}
            }
            """);
        });
        var gateway = BuildGateway(handler);

        var result = await gateway.CaptureAsync("AUTH-1", "order-1-capture", default);

        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal(20m, result.Gross);
        Assert.Equal(0.88m, result.PaypalFee);
        Assert.Equal(19.12m, result.Net);
    }
}

public class MoneyFormatterTests
{
    [Theory]
    [InlineData("USD", 12.5, "12.50")]
    [InlineData("USD", 20, "20.00")]
    [InlineData("JPY", 1300, "1300")]
    [InlineData("BHD", 1.234, "1.234")]
    public void Format_HonorsCurrencyDecimals(string currency, decimal amount, string expected)
        => Assert.Equal(expected, MoneyFormatter.Format(amount, currency));

    [Fact]
    public void Parse_RoundTrips()
        => Assert.Equal(19.12m, MoneyFormatter.Parse("19.12"));
}
