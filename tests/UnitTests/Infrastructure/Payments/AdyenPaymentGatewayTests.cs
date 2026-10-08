using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AdyenApIs;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class AdyenPaymentGatewayTests
{
    private const string AuthorisedJson = """{ "resultCode": "Authorised", "pspReference": "PSP-AUTH-1", "amount": { "currency": "USD", "value": 5100 }, "merchantReference": "ref-1" }""";
    private const string RefundReceivedJson = """{ "merchantAccount": "OfflineTestMerchant", "paymentPspReference": "PSP-AUTH-1", "pspReference": "PSP-REFUND-1", "status": "received", "amount": { "currency": "USD", "value": 1250 } }""";

    private static readonly CardDetails TestCard = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Demo Shopper");

    private static AdyenSettings Settings(TimeSpan? attemptTimeout = null) => new()
    {
        ApiKey = "offline-test-key",
        MerchantAccount = "OfflineTestMerchant",
        Environment = "test",
        Currency = "usd",
        ReturnUrl = "https://shop.example/Order/MyOrders",
        AttemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(20),
    };

    private static AdyenPaymentGateway Gateway(StubAdyenHandler handler, AdyenSettings? settings = null)
    {
        settings ??= Settings();
        var client = new AdyenApIsClient(new HttpClient(handler), AdyenClientFactory.CreateOptions(settings, NullLoggerFactory.Instance));
        return new AdyenPaymentGateway(client, Options.Create(settings), Substitute.For<IAppLogger<AdyenPaymentGateway>>());
    }

    private static CardPaymentRequest Charge(long amountMinor = 5100) =>
        new(7, "idem-key-1", "eshop-7-P1-abcdef12", "USD", amountMinor, TestCard);

    private static ProviderRefundRequest Refund(long amountMinor = 1250) =>
        new("PSP-AUTH-1", "idem-refund-1", "eshop-7-R1-abcdef12", "USD", amountMinor, "RETURN");

    [Fact]
    public async Task ChargeSendsOrderTotalEncryptedCardAndIdempotencyKey()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.OK, AuthorisedJson);

        await Gateway(handler).ChargeCardAsync(Charge(5100), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/payments", request.RequestUri!.AbsolutePath);
        Assert.Equal("idem-key-1", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
        Assert.Equal("offline-test-key", Assert.Single(request.Headers.GetValues("X-API-Key")));

        using var body = JsonDocument.Parse(handler.Bodies[0]!);
        var root = body.RootElement;
        Assert.Equal(5100, root.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("OfflineTestMerchant", root.GetProperty("merchantAccount").GetString());
        Assert.Equal("eshop-7-P1-abcdef12", root.GetProperty("reference").GetString());
        Assert.Equal(0, root.GetProperty("captureDelayHours").GetInt32());
        Assert.Equal("Ecommerce", root.GetProperty("shopperInteraction").GetString());
        Assert.Equal("https://shop.example/Order/MyOrders", root.GetProperty("returnUrl").GetString());
        var card = root.GetProperty("paymentMethod");
        Assert.Equal("scheme", card.GetProperty("type").GetString());
        Assert.Equal("test_4111111145551142", card.GetProperty("encryptedCardNumber").GetString());
        Assert.Equal("test_03", card.GetProperty("encryptedExpiryMonth").GetString());
        Assert.Equal("test_2030", card.GetProperty("encryptedExpiryYear").GetString());
        Assert.Equal("test_737", card.GetProperty("encryptedSecurityCode").GetString());
        Assert.Equal("Demo Shopper", card.GetProperty("holderName").GetString());
    }

    [Fact]
    public async Task ChargeMapsAuthorisedResult()
    {
        var result = await Gateway(StubAdyenHandler.Returning(HttpStatusCode.OK, AuthorisedJson))
            .ChargeCardAsync(Charge(), CancellationToken.None);

        Assert.Equal(CardPaymentOutcome.Authorised, result.Outcome);
        Assert.Equal("Authorised", result.ResultCode);
        Assert.Equal("PSP-AUTH-1", result.PspReference);
        Assert.Equal(5100, result.ChargedAmountMinor);
        Assert.Equal("USD", result.ChargedCurrency);
    }

    [Fact]
    public async Task ChargeMapsRefusalWithReason()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.OK,
            """{ "resultCode": "Refused", "pspReference": "PSP-REF-1", "refusalReason": "Expired Card", "refusalReasonCode": "6" }""");

        var result = await Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None);

        Assert.Equal(CardPaymentOutcome.Refused, result.Outcome);
        Assert.Equal("Expired Card", result.RefusalReason);
        Assert.Equal("6", result.RefusalReasonCode);
    }

    [Theory]
    [InlineData("RedirectShopper", CardPaymentOutcome.ActionRequired)]
    [InlineData("ChallengeShopper", CardPaymentOutcome.ActionRequired)]
    [InlineData("Error", CardPaymentOutcome.Error)]
    [InlineData("Cancelled", CardPaymentOutcome.Cancelled)]
    [InlineData("Pending", CardPaymentOutcome.Pending)]
    [InlineData("PartiallyAuthorised", CardPaymentOutcome.Unsupported)]
    [InlineData("SomethingNew", CardPaymentOutcome.Unsupported)]
    public async Task ChargeMapsOtherResultCodes(string resultCode, CardPaymentOutcome expected)
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.OK, $$"""{ "resultCode": "{{resultCode}}", "pspReference": "PSP-X" }""");

        var result = await Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(resultCode, result.ResultCode);
    }

    [Fact]
    public async Task ChargeWithoutResultCodeIsAnUnknownOutcome()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.OK, """{ "pspReference": "PSP-X" }""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, ex.Failure);
    }

    [Fact]
    public async Task ChargeValidationErrorIsRejectedWithAdyensMessage()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.UnprocessableEntity,
            """{ "status": 422, "errorCode": "174", "message": "Unable to decrypt data", "errorType": "validation", "pspReference": "PSP-ERR-1" }""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.Rejected, ex.Failure);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Equal("174", ex.ProviderErrorCode);
        Assert.Equal("PSP-ERR-1", ex.ProviderReference);
        Assert.Contains("Unable to decrypt data", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ChargeCredentialOrThrottlingErrorIsUnavailableNotTheCallersFault(HttpStatusCode status)
    {
        var handler = StubAdyenHandler.Returning(status, """{ "status": 401, "errorCode": "000", "message": "HTTP Status Response - Unauthorized", "errorType": "security" }""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.Unavailable, ex.Failure);
        Assert.DoesNotContain("Unauthorized", ex.Message);
    }

    [Fact]
    public async Task ChargeProviderErrorIsAnUnknownOutcomeAndNotResent()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.InternalServerError,
            """{ "status": 500, "errorCode": "905", "message": "internal", "errorType": "internal" }""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, ex.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ChargeUnreadableSuccessBodyIsAnUnknownOutcome()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.OK, """{ "resultCode": "Authorised", "amount": "not-an-object" }""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, ex.Failure);
        Assert.Equal(200, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task ChargeUnreadableErrorBodyKeepsTheRejection()
    {
        var handler = new StubAdyenHandler((_, _, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("<html>bad gateway page</html>", System.Text.Encoding.UTF8, "application/json")
        }));

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.Rejected, ex.Failure);
        Assert.Equal(422, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task ChargeConnectionFailureResendsOnceWithSameIdempotencyKey()
    {
        var handler = new StubAdyenHandler((_, _, attempt, _) => attempt == 1
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(StubAdyenHandler.Json(HttpStatusCode.OK, AuthorisedJson)));

        var result = await Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None);

        Assert.Equal(CardPaymentOutcome.Authorised, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("idem-key-1", Assert.Single(r.Headers.GetValues("Idempotency-Key"))));
        Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
    }

    [Fact]
    public async Task ChargeRepeatedConnectionFailureIsAnUnknownOutcomeAfterOneResend()
    {
        var handler = new StubAdyenHandler((_, _, _, _) => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, ex.Failure);
        Assert.False(ex.TimedOut);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ChargeAgainstAHungAdyenEndsAtTheCallersDeadline()
    {
        var handler = new StubAdyenHandler(async (_, _, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubAdyenHandler.Json(HttpStatusCode.OK, AuthorisedJson);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var watch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).ChargeCardAsync(Charge(), deadline.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.True(ex.TimedOut);
        Assert.Equal(PaymentGatewayFailure.OutcomeUnknown, ex.Failure);
        Assert.Contains("Adyen did not respond", ex.Message);
    }

    [Fact]
    public async Task ChargePerAttemptTimeoutResendsOnceThenReportsNoResponse()
    {
        var handler = new StubAdyenHandler(async (_, _, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubAdyenHandler.Json(HttpStatusCode.OK, AuthorisedJson);
        });
        var gateway = Gateway(handler, Settings(attemptTimeout: TimeSpan.FromMilliseconds(200)));
        var watch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.ChargeCardAsync(Charge(), CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.True(ex.TimedOut);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("Adyen did not respond", ex.Message);
    }

    [Fact]
    public async Task RefundSendsPaymentReferenceAmountAndReason()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.Created, RefundReceivedJson);

        var result = await Gateway(handler).RefundAsync(Refund(1250), CancellationToken.None);

        Assert.Equal("PSP-REFUND-1", result.PspReference);
        Assert.Equal("received", result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/payments/PSP-AUTH-1/refunds", request.RequestUri!.AbsolutePath);
        Assert.Equal("idem-refund-1", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
        using var body = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal(1250, body.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", body.RootElement.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("OfflineTestMerchant", body.RootElement.GetProperty("merchantAccount").GetString());
        Assert.Equal("eshop-7-R1-abcdef12", body.RootElement.GetProperty("reference").GetString());
        Assert.Equal("RETURN", body.RootElement.GetProperty("merchantRefundReason").GetString());
    }

    [Fact]
    public async Task RefundValidationErrorIsRejected()
    {
        var handler = StubAdyenHandler.Returning(HttpStatusCode.UnprocessableEntity,
            """{ "status": 422, "errorCode": "137", "message": "Invalid amount specified", "errorType": "validation" }""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).RefundAsync(Refund(), CancellationToken.None));

        Assert.Equal(PaymentGatewayFailure.Rejected, ex.Failure);
        Assert.Contains("Invalid amount specified", ex.Message);
    }

    [Fact]
    public async Task RefundConnectionFailureResendsOnceWithSameIdempotencyKey()
    {
        var handler = new StubAdyenHandler((_, _, attempt, _) => attempt == 1
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(StubAdyenHandler.Json(HttpStatusCode.Created, RefundReceivedJson)));

        var result = await Gateway(handler).RefundAsync(Refund(), CancellationToken.None);

        Assert.Equal("PSP-REFUND-1", result.PspReference);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("idem-refund-1", Assert.Single(r.Headers.GetValues("Idempotency-Key"))));
    }

    [Fact]
    public void CurrencyComesFromConfigurationNormalised()
    {
        Assert.Equal("USD", Gateway(StubAdyenHandler.Returning(HttpStatusCode.OK, "{}")).Currency);
    }
}
