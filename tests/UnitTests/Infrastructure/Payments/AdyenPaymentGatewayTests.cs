using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class AdyenPaymentGatewayTests
{
    private const string MerchantAccount = "TestMerchantECOM";

    private static readonly EncryptedCardDetails Card = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Test Shopper");

    private static readonly PaymentAuthorisationRequest PayRequest =
        new("ESHOP-7-PAY-1", "ESHOP-7", "key-pay-1", 5100, "USD", Card, "https://localhost/api/orders/7");

    private static readonly GatewayRefundRequest RefundRequest =
        new("ESHOP-7-REFUND-1", "key-refund-1", "PSP-PAYMENT-1", 500, "USD");

    private const string AuthorisedBody =
        """{"pspReference":"PSP-PAYMENT-1","resultCode":"Authorised","amount":{"currency":"USD","value":5100},"merchantReference":"ESHOP-7-PAY-1","futureField":{"addedLater":true}}""";

    private static (AdyenPaymentGateway Gateway, AdyenStubHandler Handler) Create(AdyenStubHandler handler, TimeSpan? attemptTimeout = null)
    {
        var settings = new AdyenSettings { ApiKey = "unit-test-key", MerchantAccount = MerchantAccount, Environment = "test", Currency = "usd" };
        var client = AdyenServiceCollectionExtensions.CreateClient(new HttpClient(handler), settings, NullLoggerFactory.Instance,
            TimeProvider.System, attemptTimeout ?? AdyenServiceCollectionExtensions.AttemptTimeout);
        var gateway = new AdyenPaymentGateway(client, Options.Create(settings), TimeProvider.System, NullLogger<AdyenPaymentGateway>.Instance);
        return (gateway, handler);
    }

    [Fact]
    public async Task Authorise_SendsOrderTotalCardAndKeys_AndReturnsAuthorised()
    {
        var (gateway, handler) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK, AuthorisedBody)));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Authorised, result.Outcome);
        Assert.Equal("PSP-PAYMENT-1", result.PspReference);
        Assert.Equal("Authorised", result.ResultCode);
        Assert.Equal(5100, result.AuthorisedAmountMinor);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/payments", request.RequestUri!.AbsolutePath);
        Assert.Equal("key-pay-1", handler.IdempotencyKeyOf(0));
        Assert.Equal("unit-test-key", request.Headers.GetValues("X-API-Key").Single());

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal(5100, root.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal(MerchantAccount, root.GetProperty("merchantAccount").GetString());
        Assert.Equal("ESHOP-7-PAY-1", root.GetProperty("reference").GetString());
        Assert.Equal(0, root.GetProperty("captureDelayHours").GetInt32());
        var method = root.GetProperty("paymentMethod");
        Assert.Equal("scheme", method.GetProperty("type").GetString());
        Assert.Equal("test_4111111145551142", method.GetProperty("encryptedCardNumber").GetString());
        Assert.Equal("test_03", method.GetProperty("encryptedExpiryMonth").GetString());
        Assert.Equal("test_2030", method.GetProperty("encryptedExpiryYear").GetString());
        Assert.Equal("test_737", method.GetProperty("encryptedSecurityCode").GetString());
        Assert.Equal("Test Shopper", method.GetProperty("holderName").GetString());
    }

    [Fact]
    public async Task Authorise_KeepsEverythingAdyenReturned_IncludingUnknownFields()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK, AuthorisedBody)));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        var exchange = Assert.Single(result.Exchanges);
        Assert.Equal(200, exchange.HttpStatusCode);
        Assert.Equal(AuthorisedBody, exchange.Body);
    }

    [Fact]
    public async Task Authorise_Refused_ReturnsRefusedWithReason()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK,
            """{"pspReference":"PSP-R","resultCode":"Refused","refusalReason":"Not enough balance","refusalReasonCode":"51"}""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Refused, result.Outcome);
        Assert.Equal("Not enough balance", result.RefusalReason);
        Assert.Equal("51", result.RefusalReasonCode);
        Assert.Null(result.AuthorisedAmountMinor);
    }

    [Fact]
    public async Task Authorise_ShopperActionRequested_ReturnsActionRequired()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK,
            """{"resultCode":"RedirectShopper"}""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.ActionRequired, result.Outcome);
    }

    [Fact]
    public async Task Authorise_UnrecognisedResultCode_IsTreatedAsPending()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK,
            """{"pspReference":"PSP-X","resultCode":"SomethingNew"}""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Pending, result.Outcome);
    }

    [Fact]
    public async Task Authorise_ValidationError_ReturnsRejectedWithAdyenMessage()
    {
        var (gateway, handler) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"errorCode":"174","message":"Unable to decrypt data","errorType":"validation","pspReference":"PSP-E"}""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Rejected, result.Outcome);
        Assert.Equal("174", result.ErrorCode);
        Assert.Equal("Unable to decrypt data", result.ErrorMessage);
        Assert.Single(handler.Requests);
        Assert.Equal(422, Assert.Single(result.Exchanges).HttpStatusCode);
    }

    [Fact]
    public async Task Authorise_Unauthorized_ReturnsProviderError()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.Unauthorized,
            """{"status":401,"errorCode":"000","message":"HTTP Status Response - Unauthorized","errorType":"security"}""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.ProviderError, result.Outcome);
    }

    [Fact]
    public async Task Authorise_ServerError_IsUnknown_AndIsNotResent()
    {
        var (gateway, handler) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.ServiceUnavailable,
            """{"status":503,"errorCode":"905","message":"Payment details are not supported","errorType":"configuration"}""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Unknown, result.Outcome);
        Assert.False(result.TimedOut);
        Assert.Single(handler.Requests); // a POST that moves money is never resent by the SDK
    }

    [Fact]
    public async Task Authorise_UnreadableSuccessBody_IsUnknownNotFailure()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK, """{"resultCode": 42""")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Unknown, result.Outcome);
        Assert.Equal("""{"resultCode": 42""", Assert.Single(result.Exchanges).Body);
    }

    [Fact]
    public async Task Authorise_ConnectionFailsThenResend_SettlesWithSameIdempotencyKey()
    {
        var (gateway, handler) = Create(new AdyenStubHandler(n => n == 1
            ? throw new HttpRequestException("connection reset")
            : AdyenStubHandler.Json(HttpStatusCode.OK, AuthorisedBody)));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Authorised, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("key-pay-1", handler.IdempotencyKeyOf(0));
        Assert.Equal("key-pay-1", handler.IdempotencyKeyOf(1));
    }

    [Fact]
    public async Task Authorise_ConnectionFailsTwice_ReturnsUnknown_AfterExactlyTwoSends()
    {
        var (gateway, handler) = Create(new AdyenStubHandler(_ => throw new HttpRequestException("connection reset")));

        var result = await gateway.AuthoriseAsync(PayRequest, CancellationToken.None);

        Assert.Equal(PaymentAuthorisationOutcome.Unknown, result.Outcome);
        Assert.True(result.TimedOut);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Authorise_AdyenNeverAnswers_ReturnsUnknownWithinBudget()
    {
        var hang = new AdyenStubHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        // Per-attempt timeout shortened so the test runs fast; the shape (send, settle-resend, give up) is production's.
        var (gateway, handler) = Create(hang, attemptTimeout: TimeSpan.FromMilliseconds(200));
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var started = DateTime.UtcNow;

        var result = await gateway.AuthoriseAsync(PayRequest, budget.Token);

        Assert.Equal(PaymentAuthorisationOutcome.Unknown, result.Outcome);
        Assert.True(result.TimedOut);
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Authorise_RequestBudgetExhausted_ReturnsUnknownImmediately()
    {
        var hang = new AdyenStubHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var (gateway, handler) = Create(hang);
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await gateway.AuthoriseAsync(PayRequest, budget.Token);

        Assert.Equal(PaymentAuthorisationOutcome.Unknown, result.Outcome);
        Assert.True(result.TimedOut);
        Assert.Single(handler.Requests); // no time left to settle within this request
    }

    [Fact]
    public async Task Refund_Received_SendsAmountAndPaymentReference()
    {
        const string body = """{"merchantAccount":"TestMerchantECOM","paymentPspReference":"PSP-PAYMENT-1","pspReference":"PSP-REFUND-1","reference":"ESHOP-7-REFUND-1","status":"received","amount":{"currency":"USD","value":500}}""";
        var (gateway, handler) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.Created, body)));

        var result = await gateway.RefundAsync(RefundRequest, CancellationToken.None);

        Assert.Equal(RefundOutcome.Received, result.Outcome);
        Assert.Equal("PSP-REFUND-1", result.PspReference);
        Assert.EndsWith("/payments/PSP-PAYMENT-1/refunds", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("key-refund-1", handler.IdempotencyKeyOf(0));
        using var sent = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(500, sent.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal(MerchantAccount, sent.RootElement.GetProperty("merchantAccount").GetString());
        Assert.Equal(body, Assert.Single(result.Exchanges).Body);
    }

    [Fact]
    public async Task Refund_Rejected_ReturnsRejected()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"errorCode":"167","message":"Original pspReference required for this operation","errorType":"validation"}""")));

        var result = await gateway.RefundAsync(RefundRequest, CancellationToken.None);

        Assert.Equal(RefundOutcome.Rejected, result.Outcome);
        Assert.Equal("167", result.ErrorCode);
    }

    [Fact]
    public async Task Refund_ConnectionFailsThenResend_SettlesWithSameIdempotencyKey()
    {
        var (gateway, handler) = Create(new AdyenStubHandler(n => n == 1
            ? throw new HttpRequestException("connection reset")
            : AdyenStubHandler.Json(HttpStatusCode.Created,
                """{"merchantAccount":"M","paymentPspReference":"PSP-PAYMENT-1","pspReference":"PSP-REFUND-1","status":"received","amount":{"currency":"USD","value":500}}""")));

        var result = await gateway.RefundAsync(RefundRequest, CancellationToken.None);

        Assert.Equal(RefundOutcome.Received, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("key-refund-1", handler.IdempotencyKeyOf(0));
        Assert.Equal("key-refund-1", handler.IdempotencyKeyOf(1));
    }

    [Fact]
    public void Currency_IsNormalisedFromSettings()
    {
        var (gateway, _) = Create(new AdyenStubHandler(_ => AdyenStubHandler.Json(HttpStatusCode.OK, "{}")));
        Assert.Equal("USD", gateway.Currency);
    }
}
