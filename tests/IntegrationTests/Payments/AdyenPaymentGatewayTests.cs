using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// The Adyen adapter driven through the real SDK client, with Adyen replaced at the HttpClient seam.
/// </summary>
public class AdyenPaymentGatewayTests
{
    private static readonly EncryptedCard TestCard = new("test_4111111145551142", "test_03", "test_2030", "test_737", "Jane Shopper");

    private static PaymentAuthorisationRequest PaymentRequest(string key = "idem-key-1") =>
        new(42, "eshop-order-42-pay-1-abcd1234", key, 369, "USD", TestCard);

    [Fact]
    public async Task AuthorisedPaymentSendsTheOrderAmountAndKeepsTheRawResponse()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.OK, AdyenTestFactory.AuthorisedJson);
        var gateway = AdyenTestFactory.Gateway(handler);

        var result = await gateway.AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Authorised, result.Outcome);
        Assert.Equal("PSP-AUTH-1", result.PspReference);
        Assert.Equal("Authorised", result.ResultCode);
        Assert.Equal(369, result.AuthorisedAmountInMinorUnits);

        var sent = Assert.Single(handler.Sent);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/payments", sent.Uri.AbsolutePath);
        Assert.Equal("idem-key-1", sent.IdempotencyKey);
        Assert.True(sent.HasApiKey);
        using var body = JsonDocument.Parse(sent.Body!);
        var root = body.RootElement;
        Assert.Equal(369, root.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("TestMerchantECOM", root.GetProperty("merchantAccount").GetString());
        Assert.Equal("eshop-order-42-pay-1-abcd1234", root.GetProperty("reference").GetString());
        Assert.Equal(0, root.GetProperty("captureDelayHours").GetInt32());
        var method = root.GetProperty("paymentMethod");
        Assert.Equal("scheme", method.GetProperty("type").GetString());
        Assert.Equal("test_4111111145551142", method.GetProperty("encryptedCardNumber").GetString());
        Assert.Equal("test_03", method.GetProperty("encryptedExpiryMonth").GetString());
        Assert.Equal("test_2030", method.GetProperty("encryptedExpiryYear").GetString());
        Assert.Equal("test_737", method.GetProperty("encryptedSecurityCode").GetString());
        Assert.Equal("Jane Shopper", method.GetProperty("holderName").GetString());

        // Everything Adyen returned is kept verbatim, including fields this build does not model.
        var captured = Assert.Single(result.Responses);
        Assert.Equal(200, captured.HttpStatus);
        Assert.Equal(AdyenTestFactory.AuthorisedJson, captured.Body);
    }

    [Fact]
    public async Task RefusedPaymentCarriesTheRefusalReason()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.OK, AdyenTestFactory.RefusedJson);

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Refused, result.Outcome);
        Assert.Equal("Not enough balance", result.RefusalReason);
        Assert.Equal("51", result.RefusalReasonCode);
    }

    [Fact]
    public async Task ShopperActionResultIsActionRequired()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.OK,
            """{"pspReference":"P","resultCode":"ChallengeShopper"}""");

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.ActionRequired, result.Outcome);
    }

    [Fact]
    public async Task ValidationErrorIsRejectedWithAdyensErrorDetails()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.UnprocessableEntity, AdyenTestFactory.ValidationErrorJson);

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Rejected, result.Outcome);
        Assert.Equal("101", result.ErrorCode);
        Assert.Equal("Invalid card number", result.ErrorMessage);
        Assert.Equal(422, result.HttpStatus);
        Assert.Equal(AdyenTestFactory.ValidationErrorJson, Assert.Single(result.Responses).Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderCallOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.Forbidden, ProviderCallOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderCallOutcome.Unknown)]
    [InlineData(HttpStatusCode.BadGateway, ProviderCallOutcome.Unknown)]
    public async Task ErrorStatusesAreClassified(HttpStatusCode status, ProviderCallOutcome expected)
    {
        var handler = new ScriptedAdyenHandler().ThenJson(status, """{"status":0,"errorCode":"000","message":"x","errorType":"internal"}""");

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.False(result.NoResponse);
        Assert.Single(handler.Sent); // a POST is never resent by the SDK
    }

    [Fact]
    public async Task UnreadableSuccessBodyIsAnUnknownOutcome()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.OK, "<html>proxy page</html>");

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Unknown, result.Outcome);
        Assert.Equal("<html>proxy page</html>", Assert.Single(result.Responses).Body);
    }

    [Fact]
    public async Task ConnectionFailureIsAnUnknownOutcomeAndIsNotResent()
    {
        var handler = new ScriptedAdyenHandler().ThenConnectionFailure();

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Unknown, result.Outcome);
        Assert.True(result.NoResponse);
        Assert.Single(handler.Sent);
        Assert.Null(Assert.Single(result.Responses).HttpStatus);
    }

    [Fact]
    public async Task HungAdyenIsCutOffByThePerAttemptTimeout()
    {
        var handler = new ScriptedAdyenHandler().ThenHang();
        var stopwatch = Stopwatch.StartNew();

        var result = await AdyenTestFactory.Gateway(handler, attemptTimeout: TimeSpan.FromMilliseconds(500))
            .AuthoriseAsync(PaymentRequest(), CancellationToken.None);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Equal(ProviderCallOutcome.Unknown, result.Outcome);
        Assert.True(result.NoResponse);
        Assert.Contains("No response from Adyen", Assert.Single(result.Responses).Note);
    }

    [Fact]
    public async Task HungAdyenIsCutOffByTheCallersBudget()
    {
        var handler = new ScriptedAdyenHandler().ThenHang();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        var result = await AdyenTestFactory.Gateway(handler).AuthoriseAsync(PaymentRequest(), budget.Token);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Equal(ProviderCallOutcome.Unknown, result.Outcome);
        Assert.True(result.NoResponse);
    }

    [Fact]
    public async Task RefundSendsTheAmountToThePaymentsRefundRouteWithTheIdempotencyKey()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.Created, AdyenTestFactory.RefundReceivedJson("PSP-AUTH-1", 150));
        var request = new ProviderRefundRequest(42, "PSP-AUTH-1", "eshop-order-42-refund-1-x", "refund-key-1", 150, "USD", RefundReason.CustomerRequest);

        var result = await AdyenTestFactory.Gateway(handler).RefundAsync(request, CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Accepted, result.Outcome);
        Assert.Equal("PSP-REFUND-1", result.PspReference);
        var sent = Assert.Single(handler.Sent);
        Assert.EndsWith("/payments/PSP-AUTH-1/refunds", sent.Uri.AbsolutePath);
        Assert.Equal("refund-key-1", sent.IdempotencyKey);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal(150, body.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", body.RootElement.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("CUSTOMER REQUEST", body.RootElement.GetProperty("merchantRefundReason").GetString());
        Assert.Contains("anotherNewField", Assert.Single(result.Responses).Body);
    }

    [Fact]
    public async Task RejectedRefundCarriesAdyensError()
    {
        var handler = new ScriptedAdyenHandler().ThenJson(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"errorCode":"167","message":"Original pspReference required for this operation","errorType":"validation"}""");
        var request = new ProviderRefundRequest(42, "PSP-AUTH-1", "r", "k", 150, "USD", null);

        var result = await AdyenTestFactory.Gateway(handler).RefundAsync(request, CancellationToken.None);

        Assert.Equal(ProviderCallOutcome.Rejected, result.Outcome);
        Assert.Equal("167", result.ErrorCode);
        Assert.Equal(422, result.HttpStatus);
    }
}
