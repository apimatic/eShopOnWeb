using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Adyen;

/// <summary>
/// Drives the real Adyen SDK client, registered exactly as the app registers it, against a stub at the HttpClient
/// seam. No network.
/// </summary>
public class AdyenPaymentGatewayTests
{
    private const string MerchantAccount = "UnitTestMerchant";

    private static readonly EncryptedCard TestCard = new("test_4111111145551142", "test_03", "test_2030", "test_737", "John Smith");

    private static (IPaymentGateway Gateway, StubAdyenHandler Handler) CreateGateway(StubAdyenHandler handler, int timeoutSeconds = 20)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Adyen:ApiKey"] = "unit-test-api-key",
            ["Adyen:MerchantAccount"] = MerchantAccount,
            ["Adyen:Environment"] = "test",
            ["Adyen:Currency"] = "USD",
            ["Adyen:TimeoutSeconds"] = timeoutSeconds.ToString(),
            ["baseUrls:webBase"] = "https://shop.example.com/",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdyenPayments(configuration);
        services.AddHttpClient(AdyenServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IPaymentGateway>(), handler);
    }

    private static CardPaymentRequest PaymentRequest(long amountMinor = 5100) =>
        new("6f1c2c1e-3a59-4c1b-9a4f-6e8f4b8f2a10", "eshop-order-7-payment-1-6f1c2c1e", amountMinor, "USD", TestCard);

    [Fact]
    public async Task SendsTheChargeAsAdyenExpectsIt()
    {
        var (gateway, handler) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.OK,
            """{"pspReference":"PSP123","resultCode":"Authorised","amount":{"currency":"USD","value":5100}}""")));

        await gateway.ChargeCardAsync(PaymentRequest());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://checkout-test.adyen.com/v71/payments", request.RequestUri!.ToString());
        Assert.Equal("unit-test-api-key", request.Headers.GetValues("X-API-Key").Single());
        Assert.Equal("6f1c2c1e-3a59-4c1b-9a4f-6e8f4b8f2a10", request.Headers.GetValues("Idempotency-Key").Single());

        using var body = JsonDocument.Parse(handler.Bodies.Single()!);
        var root = body.RootElement;
        Assert.Equal(5100, root.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal(MerchantAccount, root.GetProperty("merchantAccount").GetString());
        Assert.Equal("eshop-order-7-payment-1-6f1c2c1e", root.GetProperty("reference").GetString());
        Assert.Equal(0, root.GetProperty("captureDelayHours").GetInt32());
        Assert.Equal("https://shop.example.com/order/my-orders", root.GetProperty("returnUrl").GetString());
        var method = root.GetProperty("paymentMethod");
        Assert.Equal("scheme", method.GetProperty("type").GetString());
        Assert.Equal("test_4111111145551142", method.GetProperty("encryptedCardNumber").GetString());
        Assert.Equal("test_03", method.GetProperty("encryptedExpiryMonth").GetString());
        Assert.Equal("test_2030", method.GetProperty("encryptedExpiryYear").GetString());
        Assert.Equal("test_737", method.GetProperty("encryptedSecurityCode").GetString());
        Assert.Equal("John Smith", method.GetProperty("holderName").GetString());
        Assert.False(method.TryGetProperty("number", out _));
    }

    [Fact]
    public async Task AuthorisedPaymentKeepsAdyensResponseVerbatimIncludingUnknownFields()
    {
        const string json = """{"pspReference":"PSP123","resultCode":"Authorised","amount":{"currency":"USD","value":5100},"someFutureField":{"nested":[1,2,3]}}""";
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.OK, json)));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Authorised, result.Outcome);
        Assert.Equal("PSP123", result.PspReference);
        Assert.Equal("Authorised", result.ResultCode);
        Assert.Equal(200, result.Exchange.HttpStatus);
        Assert.Equal(json, result.Exchange.ResponseBody);
    }

    [Fact]
    public async Task RefusedCardCarriesAdyensReason()
    {
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.OK,
            """{"pspReference":"PSP9","resultCode":"Refused","refusalReason":"Expired Card","refusalReasonCode":"6"}""")));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Refused, result.Outcome);
        Assert.Equal("Expired Card", result.RefusalReason);
        Assert.Equal("6", result.RefusalReasonCode);
    }

    [Theory]
    [InlineData("RedirectShopper", CardPaymentOutcome.ActionRequired)]
    [InlineData("Pending", CardPaymentOutcome.Pending)]
    [InlineData("Error", CardPaymentOutcome.Failed)]
    [InlineData("SomethingAdyenAddsLater", CardPaymentOutcome.Pending)]
    public async Task MapsResultCodes(string resultCode, CardPaymentOutcome expected)
    {
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.OK,
            $$"""{"pspReference":"PSP1","resultCode":"{{resultCode}}"}""")));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task AuthorisationForADifferentAmountIsHeldForReview()
    {
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.OK,
            """{"pspReference":"PSP1","resultCode":"Authorised","amount":{"currency":"USD","value":100}}""")));

        var result = await gateway.ChargeCardAsync(PaymentRequest(5100));

        Assert.Equal(CardPaymentOutcome.Pending, result.Outcome);
    }

    [Fact]
    public async Task ValidationErrorIsARejectionWithAdyensErrorAndBody()
    {
        const string json = """{"status":422,"errorCode":"174","message":"Unable to decrypt data","errorType":"validation","pspReference":"PSP7"}""";
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.UnprocessableEntity, json)));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Rejected, result.Outcome);
        Assert.False(result.IsConfigurationError);
        Assert.Equal("174", result.ErrorCode);
        Assert.Equal("Unable to decrypt data", result.ErrorMessage);
        Assert.Equal(422, result.Exchange.HttpStatus);
        Assert.Equal(json, result.Exchange.ResponseBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task CredentialFailureIsOurProblemNotTheShoppers(HttpStatusCode status)
    {
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(status,
            """{"status":401,"errorCode":"000","message":"HTTP Status Response - Unauthorized","errorType":"security"}""")));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Rejected, result.Outcome);
        Assert.True(result.IsConfigurationError);
    }

    [Fact]
    public async Task ServerErrorLeavesTheOutcomeUnknown()
    {
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.InternalServerError,
            """{"status":500,"errorCode":"905","message":"Payment details are not supported","errorType":"configuration"}""")));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Unknown, result.Outcome);
        Assert.Equal(500, result.Exchange.HttpStatus);
    }

    [Fact]
    public async Task ConnectionFailureIsUnknownAndThePaymentIsNeverResentByTheSdk()
    {
        var (gateway, handler) = CreateGateway(new StubAdyenHandler((_, _) => throw new HttpRequestException("connection reset")));

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Unknown, result.Outcome);
        Assert.Null(result.Exchange.HttpStatus);
        Assert.Contains("connection reset", result.Exchange.TransportError);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TimeoutIsUnknown()
    {
        var (gateway, handler) = CreateGateway(new StubAdyenHandler(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubAdyenHandler.Json(HttpStatusCode.OK, "{}");
        }), timeoutSeconds: 1);

        var result = await gateway.ChargeCardAsync(PaymentRequest());

        Assert.Equal(CardPaymentOutcome.Unknown, result.Outcome);
        Assert.NotNull(result.Exchange.TransportError);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendsTheRefundAgainstTheCapturedPayment()
    {
        const string json = """{"merchantAccount":"UnitTestMerchant","paymentPspReference":"PSP123","pspReference":"REF456","reference":"eshop-order-7-refund-1","status":"received","amount":{"currency":"USD","value":1050},"futureField":true}""";
        var (gateway, handler) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.Created, json)));

        var result = await gateway.RefundAsync(new RefundRequest("8a7b6c5d-0000-4000-8000-000000000001", "eshop-order-7-refund-1", "PSP123", 1050, "USD"));

        Assert.Equal(RefundOutcome.Received, result.Outcome);
        Assert.Equal("REF456", result.PspReference);
        Assert.Equal(json, result.Exchange.ResponseBody);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://checkout-test.adyen.com/v71/payments/PSP123/refunds", request.RequestUri!.ToString());
        Assert.Equal("8a7b6c5d-0000-4000-8000-000000000001", request.Headers.GetValues("Idempotency-Key").Single());
        using var body = JsonDocument.Parse(handler.Bodies.Single()!);
        Assert.Equal(1050, body.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", body.RootElement.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal(MerchantAccount, body.RootElement.GetProperty("merchantAccount").GetString());
        Assert.Equal("eshop-order-7-refund-1", body.RootElement.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task RejectedRefundCarriesAdyensMessage()
    {
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"errorCode":"167","message":"Original pspReference required for this operation","errorType":"validation"}""")));

        var result = await gateway.RefundAsync(new RefundRequest("k", "ref", "PSP123", 100, "USD"));

        Assert.Equal(RefundOutcome.Rejected, result.Outcome);
        Assert.Equal("167", result.ErrorCode);
        Assert.Equal("Original pspReference required for this operation", result.ErrorMessage);
    }

    [Fact]
    public async Task UnreadableSuccessfulRefundResponseIsUnknownNotFailed()
    {
        // 201 without the required pspReference: the SDK cannot build its model.
        var (gateway, _) = CreateGateway(new StubAdyenHandler((_, _) => StubAdyenHandler.Json(HttpStatusCode.Created, """{"status":"received"}""")));

        var result = await gateway.RefundAsync(new RefundRequest("k", "ref", "PSP123", 100, "USD"));

        Assert.Equal(RefundOutcome.Unknown, result.Outcome);
        Assert.Equal("""{"status":"received"}""", result.Exchange.ResponseBody);
    }

    [Fact]
    public async Task RefundConnectionFailureIsUnknown()
    {
        var (gateway, handler) = CreateGateway(new StubAdyenHandler((_, _) => throw new HttpRequestException("connection reset")));

        var result = await gateway.RefundAsync(new RefundRequest("k", "ref", "PSP123", 100, "USD"));

        Assert.Equal(RefundOutcome.Unknown, result.Outcome);
        Assert.Single(handler.Requests);
    }
}
