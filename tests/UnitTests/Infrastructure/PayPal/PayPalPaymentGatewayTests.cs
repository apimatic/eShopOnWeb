using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.PayPal;

public class PayPalPaymentGatewayTests
{
    [Fact]
    public async Task CaptureReportsWhatPayPalSaidTheFeeAndNetProceedsWere()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """
        {
          "id": "CAP-1",
          "status": "COMPLETED",
          "amount": { "currency_code": "USD", "value": "17.00" },
          "seller_receivable_breakdown": {
            "gross_amount": { "currency_code": "USD", "value": "17.00" },
            "paypal_fee": { "currency_code": "USD", "value": "0.93" },
            "net_amount": { "currency_code": "USD", "value": "16.07" }
          }
        }
        """);

        var result = await GatewayFactory.Create(handler)
            .CaptureAsync("AUTH-1", 17.00m, "eshop-1-x", "key-1", default);

        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(17.00m, result.Amount);
        Assert.Equal(0.93m, result.PayPalFee);
        Assert.Equal(16.07m, result.NetAmount);
    }

    [Fact]
    public async Task CaptureAsksForTheFullRepresentationAndSendsTheCallersIdempotencyKey()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """
        { "id": "CAP-1", "status": "COMPLETED", "amount": { "currency_code": "USD", "value": "17.00" } }
        """);

        await GatewayFactory.Create(handler).CaptureAsync("AUTH-1", 17.00m, "eshop-1-x", "key-1", default);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/v2/payments/authorizations/AUTH-1/capture", request.RequestUri!.AbsolutePath);

        // return=minimal would omit the seller_receivable_breakdown the test above depends on.
        Assert.Equal("return=representation", request.Headers.GetValues("Prefer").Single());
        Assert.Equal("key-1", request.Headers.GetValues("PayPal-Request-Id").Single());

        // final_capture releases whatever is left on the hold rather than stranding it.
        Assert.Contains("\"final_capture\":true", handler.Bodies.Single());
    }

    [Fact]
    public async Task ACaptureIsNeverResentWhenTheConnectionFails()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection reset"));

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => GatewayFactory.Create(handler)
            .CaptureAsync("AUTH-1", 17.00m, "eshop-1-x", "key-1", default));

        // The SDK's default HttpMethodsToRetry excludes POST, and this is the guarantee that stops a
        // transport blip from capturing the shopper twice. It is configuration, so it is worth a test.
        Assert.Single(handler.Requests);

        // The bytes may have reached PayPal, so the outcome is unknown — not "it failed".
        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, failure.Kind);
    }

    [Fact]
    public async Task ARejectedRefundCarriesPayPalsOwnErrorCodeAndCorrelationId()
    {
        var handler = new StubHandler(HttpStatusCode.UnprocessableEntity, """
        {
          "name": "UNPROCESSABLE_ENTITY",
          "message": "The requested action could not be performed.",
          "debug_id": "abc123def456",
          "details": [ { "issue": "REFUND_AMOUNT_EXCEEDED", "description": "The refund amount must be less than or equal to the capture amount." } ]
        }
        """);

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => GatewayFactory.Create(handler)
            .RefundAsync("CAP-1", 100m, "key-1", default));

        Assert.Equal(PaymentGatewayFailure.Conflict, failure.Kind);
        Assert.Equal("REFUND_AMOUNT_EXCEEDED", failure.ProviderCode);
        // debug_id is what PayPal support correlates on, so it must survive the translation.
        Assert.Equal("abc123def456", failure.DebugId);
        Assert.Contains("refund amount must be less than", failure.Message);
    }

    [Fact]
    public async Task AnAuthenticationFailureIsNeverReportedAsTheCallersFault()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """
        { "name": "AUTHENTICATION_FAILURE", "message": "Authentication failed.", "debug_id": "d1" }
        """);

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => GatewayFactory.Create(handler)
            .RefundAsync("CAP-1", 1m, "key-1", default));

        // Our credentials are wrong; the caller did nothing and can fix nothing.
        Assert.Equal(PaymentGatewayFailure.Unavailable, failure.Kind);
    }

    [Fact]
    public async Task ASuccessBodyThatCannotBeReadIsAnUnknownOutcome_NotASilentSuccess()
    {
        // A 2xx whose body no longer matches the model surfaces as a JsonException from
        // deserialization, which an SdkException-only catch ladder would let escape.
        var handler = new StubHandler(HttpStatusCode.Created, "{ \"amount\": \"not-an-object\" }");

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => GatewayFactory.Create(handler)
            .CaptureAsync("AUTH-1", 17m, "eshop-1-x", "key-1", default));

        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, failure.Kind);
    }

    [Fact]
    public async Task AnAuthorizationChallengeIsSurfacedRatherThanWorkedAround()
    {
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/authorize")
            ? StubHandler.Json(HttpStatusCode.Created, """{ "id": "ORD-1", "status": "PAYER_ACTION_REQUIRED" }""")
            : StubHandler.Json(HttpStatusCode.Created, """{ "id": "ORD-1", "status": "CREATED" }"""));

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => GatewayFactory.Create(handler)
            .AuthorizeAsync(NewAuthorization(), default));

        Assert.Equal(PaymentGatewayFailure.ApprovalRequired, failure.Kind);
        Assert.Contains("browser", failure.Message);
    }

    [Fact]
    public async Task PayingWithASavedCardSendsOnlyTheVaultReference()
    {
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/authorize")
            ? StubHandler.Json(HttpStatusCode.Created, """
              {
                "id": "ORD-1", "status": "COMPLETED",
                "purchase_units": [ { "payments": { "authorizations": [
                  { "id": "AUTH-1", "status": "CREATED", "amount": { "currency_code": "USD", "value": "17.00" } }
                ] } } ]
              }
              """)
            : StubHandler.Json(HttpStatusCode.Created, """{ "id": "ORD-1", "status": "CREATED" }"""));

        var request = NewAuthorization() with { Instrument = new PaymentInstrument.VaultToken("VAULT-9") };
        var result = await GatewayFactory.Create(handler).AuthorizeAsync(request, default);

        Assert.Equal("AUTH-1", result.AuthorizationId);

        var authorizeBody = handler.Bodies.Last()!;
        Assert.Contains("\"vault_id\":\"VAULT-9\"", authorizeBody);
        Assert.DoesNotContain("\"number\"", authorizeBody);
    }

    [Fact]
    public async Task AnAuthorizedOrderWithNoAuthorizationIdIsAnUnknownOutcome()
    {
        var handler = new StubHandler(HttpStatusCode.Created,
            """{ "id": "ORD-1", "status": "COMPLETED", "purchase_units": [ { } ] }""");

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => GatewayFactory.Create(handler)
            .AuthorizeAsync(NewAuthorization(), default));

        // Funds may be held under an id we cannot see; calling that a failure orphans the hold.
        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, failure.Kind);
    }

    private static AuthorizationRequest NewAuthorization() => new()
    {
        OrderId = 1,
        InvoiceId = "eshop-1-20260828120000",
        Amount = 17.00m,
        Description = "eShopOnWeb order 1",
        Instrument = new PaymentInstrument.OneOffCard(new CardDetails
        {
            Number = "4111111111111111",
            Expiry = "2030-01",
            SecurityCode = "123"
        }),
        CreateIdempotencyKey = "create-1",
        AuthorizeIdempotencyKey = "auth-1"
    };
}
