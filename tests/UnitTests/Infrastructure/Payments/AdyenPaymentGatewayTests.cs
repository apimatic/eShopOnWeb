using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs;
using AdyenApIs.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class AdyenPaymentGatewayTests
{
    private const string MerchantAccount = "TestMerchantECOM";

    private static readonly ChargeCommand Charge = new(
        IdempotencyKey: "0123456789abcdef0123456789abcdef",
        MerchantReference: "eshop-order-7-1",
        AmountMinorUnits: 12_345,
        Currency: "USD",
        Card: new EncryptedCard("test_4111111145551142", "test_03", "test_2030", "test_737", "Jane Shopper"),
        ReturnUrl: "https://localhost/api/orders/7");

    private static readonly RefundCommand Refund = new(
        PaymentPspReference: "PSP0000000000001",
        IdempotencyKey: "fedcba9876543210fedcba9876543210",
        Reference: "rf_7_abc",
        AmountMinorUnits: 500,
        Currency: "USD");

    private static (AdyenPaymentGateway Gateway, StubHandler Handler) CreateGateway(TimeSpan? httpTimeout = null)
    {
        var handler = new StubHandler();
        var httpClient = new HttpClient(handler) { Timeout = httpTimeout ?? TimeSpan.FromSeconds(30) };
        var client = new AdyenApIsClient(httpClient, new AdyenApIsClientOptions
        {
            ApiKeyAuth = "unit-test-key",
            Retry = RetryOptions.Disabled() with { Timeout = null },
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance },
        });
        var settings = Options.Create(new AdyenSettings
        {
            ApiKey = "unit-test-key",
            MerchantAccount = MerchantAccount,
            Environment = "test",
            Currency = "USD"
        });
        return (new AdyenPaymentGateway(client, settings, NullLogger<AdyenPaymentGateway>.Instance), handler);
    }

    private const string AuthorisedJson = """{ "pspReference": "PSP0000000000001", "resultCode": "Authorised", "amount": { "currency": "USD", "value": 12345 }, "merchantReference": "eshop-order-7-1" }""";
    private const string RefundReceivedJson = """{ "merchantAccount": "TestMerchantECOM", "paymentPspReference": "PSP0000000000001", "pspReference": "RFND000000000001", "reference": "rf_7_abc", "status": "received", "amount": { "currency": "USD", "value": 500 } }""";

    [Fact]
    public async Task Charge_sends_card_payment_for_exact_amount_with_idempotency_key_and_immediate_capture()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.OK, AuthorisedJson);

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Authorised, result.Status);
        Assert.Equal("PSP0000000000001", result.PspReference);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://checkout-test.adyen.com/v71/payments", request.RequestUri!.ToString());
        Assert.Equal(Charge.IdempotencyKey, handler.HeaderOf(0, "Idempotency-Key"));
        Assert.Equal("unit-test-key", handler.HeaderOf(0, "X-API-Key"));

        using var body = JsonDocument.Parse(handler.Bodies[0]!);
        var root = body.RootElement;
        Assert.Equal(12_345, root.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal(MerchantAccount, root.GetProperty("merchantAccount").GetString());
        Assert.Equal("eshop-order-7-1", root.GetProperty("reference").GetString());
        Assert.Equal(0, root.GetProperty("captureDelayHours").GetInt32());
        var paymentMethod = root.GetProperty("paymentMethod");
        Assert.Equal("scheme", paymentMethod.GetProperty("type").GetString());
        Assert.Equal("test_4111111145551142", paymentMethod.GetProperty("encryptedCardNumber").GetString());
        Assert.Equal("test_03", paymentMethod.GetProperty("encryptedExpiryMonth").GetString());
        Assert.Equal("test_2030", paymentMethod.GetProperty("encryptedExpiryYear").GetString());
        Assert.Equal("test_737", paymentMethod.GetProperty("encryptedSecurityCode").GetString());
        Assert.Equal("Jane Shopper", paymentMethod.GetProperty("holderName").GetString());
    }

    [Fact]
    public async Task Refused_card_is_declined_with_reason_shopper_can_act_on()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.OK, """{ "pspReference": "PSP0000000000002", "resultCode": "Refused", "refusalReason": "Not enough balance", "refusalReasonCode": "51" }""");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Declined, result.Status);
        Assert.Equal("Not enough balance", result.RefusalReason);
        Assert.Contains("Not enough balance", result.ShopperMessage);
        Assert.Contains("use a different card", result.ShopperMessage);
        Assert.Contains("No money was taken", result.ShopperMessage);
    }

    [Fact]
    public async Task Redirect_or_3ds_result_is_declined_as_unsupported_action()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.OK, """{ "resultCode": "RedirectShopper" }""");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Declined, result.Status);
        Assert.Contains("different card", result.ShopperMessage);
    }

    [Fact]
    public async Task Validation_error_is_reported_as_invalid_card_details()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.UnprocessableEntity, """{ "status": 422, "errorCode": "101", "message": "Invalid card number", "errorType": "validation", "pspReference": "ERR0000000000001" }""");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Invalid, result.Status);
        Assert.Contains("Invalid card number", result.ShopperMessage);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Unauthorized_is_a_provider_error_and_is_not_resent()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.Unauthorized, """{ "status": 401, "errorCode": "000", "message": "HTTP Status Response - Unauthorized", "errorType": "security" }""");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.ProviderError, result.Status);
        Assert.DoesNotContain("Unauthorized", result.ShopperMessage);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Charge_connection_failure_then_success_resends_same_idempotency_key()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenThrow(new HttpRequestException("connection reset"))
            .ThenJson(HttpStatusCode.OK, AuthorisedJson);

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Authorised, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Charge.IdempotencyKey, handler.HeaderOf(0, "Idempotency-Key"));
        Assert.Equal(Charge.IdempotencyKey, handler.HeaderOf(1, "Idempotency-Key"));
    }

    [Fact]
    public async Task Charge_connection_failure_twice_reports_unknown()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenThrow(new HttpRequestException("connection reset"))
            .ThenThrow(new HttpRequestException("connection reset"));

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Unknown, result.Status);
        Assert.False(result.TimedOut);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("never be charged twice", result.ShopperMessage);
    }

    [Fact]
    public async Task Charge_that_outlives_the_deadline_reports_adyen_did_not_respond()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenHang();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await gateway.ChargeAsync(Charge, deadline.Token);

        Assert.Equal(ChargeStatus.Unknown, result.Status);
        Assert.True(result.TimedOut);
        Assert.Contains("Adyen did not respond", result.ShopperMessage);
        Assert.Single(handler.Requests); // no budget left, so no resend
    }

    [Fact]
    public async Task Server_error_is_settled_by_resending_with_same_key()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.InternalServerError, """{ "status": 500, "errorCode": "905", "message": "Payment details are not supported", "errorType": "configuration" }""")
            .ThenJson(HttpStatusCode.OK, AuthorisedJson);

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Authorised, result.Status);
        Assert.Equal(Charge.IdempotencyKey, handler.HeaderOf(1, "Idempotency-Key"));
    }

    [Fact]
    public async Task Unreadable_success_body_is_an_unknown_outcome_not_a_failure()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.OK, "<html>proxy page</html>")
            .ThenJson(HttpStatusCode.OK, "<html>proxy page</html>");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Unknown, result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Partial_authorisation_is_reversed()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.OK, """{ "pspReference": "PSP0000000000003", "resultCode": "PartiallyAuthorised", "amount": { "currency": "USD", "value": 5000 } }""")
            .ThenJson(HttpStatusCode.Created, """{ "merchantAccount": "TestMerchantECOM", "paymentPspReference": "PSP0000000000003", "pspReference": "REV0000000000001", "status": "received" }""");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Reversed, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/payments/PSP0000000000003/reversals", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("rev-" + Charge.IdempotencyKey, handler.HeaderOf(1, "Idempotency-Key"));
    }

    [Fact]
    public async Task Authorisation_for_a_different_amount_is_reversed()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.OK, """{ "pspReference": "PSP0000000000004", "resultCode": "Authorised", "amount": { "currency": "USD", "value": 12000 } }""")
            .ThenJson(HttpStatusCode.Created, """{ "merchantAccount": "TestMerchantECOM", "paymentPspReference": "PSP0000000000004", "pspReference": "REV0000000000002", "status": "received" }""");

        var result = await gateway.ChargeAsync(Charge, CancellationToken.None);

        Assert.Equal(ChargeStatus.Reversed, result.Status);
    }

    [Fact]
    public async Task Refund_sends_amount_reference_and_key_to_the_payment_being_refunded()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.Created, RefundReceivedJson);

        var result = await gateway.RefundAsync(Refund, CancellationToken.None);

        Assert.Equal(RefundGatewayStatus.Received, result.Status);
        Assert.Equal("RFND000000000001", result.PspReference);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://checkout-test.adyen.com/v71/payments/PSP0000000000001/refunds", request.RequestUri!.ToString());
        Assert.Equal(Refund.IdempotencyKey, handler.HeaderOf(0, "Idempotency-Key"));
        using var body = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal(500, body.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.Equal("USD", body.RootElement.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal(MerchantAccount, body.RootElement.GetProperty("merchantAccount").GetString());
        Assert.Equal("rf_7_abc", body.RootElement.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Refund_timeout_then_success_resends_same_idempotency_key()
    {
        var (gateway, handler) = CreateGateway(httpTimeout: TimeSpan.FromMilliseconds(300));
        handler.ThenHang().ThenJson(HttpStatusCode.Created, RefundReceivedJson);

        var result = await gateway.RefundAsync(Refund, CancellationToken.None);

        Assert.Equal(RefundGatewayStatus.Received, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Refund.IdempotencyKey, handler.HeaderOf(0, "Idempotency-Key"));
        Assert.Equal(Refund.IdempotencyKey, handler.HeaderOf(1, "Idempotency-Key"));
    }

    [Fact]
    public async Task Refund_timing_out_twice_reports_adyen_did_not_respond()
    {
        var (gateway, handler) = CreateGateway(httpTimeout: TimeSpan.FromMilliseconds(200));
        handler.ThenHang().ThenHang();

        var result = await gateway.RefundAsync(Refund, CancellationToken.None);

        Assert.Equal(RefundGatewayStatus.Unknown, result.Status);
        Assert.True(result.TimedOut);
        Assert.Contains("Adyen did not respond", result.Message);
    }

    [Fact]
    public async Task Refund_rejected_by_adyen_is_rejected()
    {
        var (gateway, handler) = CreateGateway();
        handler.ThenJson(HttpStatusCode.UnprocessableEntity, """{ "status": 422, "errorCode": "167", "message": "Original pspReference required for this operation", "errorType": "validation" }""");

        var result = await gateway.RefundAsync(Refund, CancellationToken.None);

        Assert.Equal(RefundGatewayStatus.Rejected, result.Status);
        Assert.Contains("Original pspReference required", result.Message);
    }

    [Fact]
    public void Card_data_never_appears_in_string_form()
    {
        Assert.DoesNotContain("4111", Charge.Card.ToString());
        Assert.DoesNotContain("4111", Charge.ToString());
    }
}
