using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;   // RawError
using PayPalServerSdk.Core.Exceptions;       // SdkException<TError>
using PayPalServerSdk.Errors;                // per-operation {Operation}Error types
using PPEnums = PayPalServerSdk.Models.Enums;
using PPModels = PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal implementation of <see cref="IPayPalPaymentGateway"/> over the APIMatic-generated
/// <c>AsadAli.Checkout.Sdk</c>. All SDK types are confined to this class — the interface and its DTOs
/// stay provider-neutral. Every SDK call is wrapped so that no raw SDK/transport exception (and no card
/// data) escapes: provider failures surface as <see cref="PaymentGatewayException"/> with a safe message
/// and the PayPal <c>debug_id</c> when available.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly Microsoft.Extensions.Logging.ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client,
        Microsoft.Extensions.Logging.ILogger<PayPalPaymentGateway> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ----------------------------------------------------------------- ChargeCardAsync

    public async Task<PaymentAuthorization> ChargeCardAsync(Money amount, CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var createBody = BuildCreateOrderBody(amount);
        var captureBody = new PPModels.OrderCaptureRequest
        {
            PaymentSource = new PPModels.OrderCaptureRequestPaymentSource
            {
                Card = new PPModels.CardRequest
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: createBody,
                ct: cancellationToken);

            var orderId = created.Id
                ?? throw new PaymentGatewayException("The payment provider did not return an order id.");

            var captured = await _client.Orders.CaptureOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: captureBody,
                prefer: "return=representation",
                ct: cancellationToken);

            return BuildAuthorization(orderId, captured, card);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToPaymentException("The payment provider rejected the order.", DebugIdOf(ex.Error), ex);
        }
        catch (SdkException<CaptureOrderError> ex)
        {
            throw ToPaymentException("The card payment was declined by the payment provider.", DebugIdOf(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    // ----------------------------------------------------------------- ChargeVaultedCardAsync

    public async Task<PaymentAuthorization> ChargeVaultedCardAsync(Money amount, string vaultId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var createBody = BuildCreateOrderBody(amount);
        // Trap A: a vaulted CARD is referenced by card.vault_id — NOT the Token payment source
        // (TokenType's only value is BILLING_AGREEMENT, which is for PayPal-wallet billing agreements).
        var captureBody = new PPModels.OrderCaptureRequest
        {
            PaymentSource = new PPModels.OrderCaptureRequestPaymentSource
            {
                Card = new PPModels.CardRequest { VaultId = vaultId }
            }
        };

        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: createBody,
                ct: cancellationToken);

            var orderId = created.Id
                ?? throw new PaymentGatewayException("The payment provider did not return an order id.");

            var captured = await _client.Orders.CaptureOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: captureBody,
                prefer: "return=representation",
                ct: cancellationToken);

            return BuildAuthorization(orderId, captured, inputCard: null);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToPaymentException("The payment provider rejected the order.", DebugIdOf(ex.Error), ex);
        }
        catch (SdkException<CaptureOrderError> ex)
        {
            throw ToPaymentException("The saved-card payment was declined by the payment provider.", DebugIdOf(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    // ----------------------------------------------------------------- RefundAsync

    public async Task<RefundReceipt> RefundAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Full refund: empty body (null) — see map RefundRequest / RefundCapturedPayment.
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                ct: cancellationToken);

            if (refund.Status == PPEnums.RefundStatus.Failed || refund.Status == PPEnums.RefundStatus.Cancelled)
            {
                throw new PaymentGatewayException("The payment provider did not complete the refund.");
            }

            var refundId = refund.Id
                ?? throw new PaymentGatewayException("The payment provider did not return a refund id.");

            return new RefundReceipt(refundId);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw ToPaymentException("The payment provider rejected the refund.", DebugIdOf(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    // ----------------------------------------------------------------- VaultCardAsync

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var oneStepBody = new PPModels.PaymentTokenRequest
        {
            PaymentSource = new PPModels.PaymentTokenRequestPaymentSource
            {
                Card = new PPModels.PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            try
            {
                var resp = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: oneStepBody,
                    ct: cancellationToken);

                return BuildVaultedCard(resp);
            }
            catch (SdkException<CreatePaymentTokenError> ex) when (!IsServerError(ex.Error))
            {
                // One-step raw-card vaulting was rejected (not a 5xx). Fall back to the documented
                // two-step flow: CreateSetupToken -> CreatePaymentToken(Token = SETUP_TOKEN).
                return await VaultViaSetupTokenAsync(card, idempotencyKey, ex, cancellationToken);
            }
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw ToPaymentException("The payment provider could not save the card.", DebugIdOf(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    private async Task<VaultedCard> VaultViaSetupTokenAsync(CardDetails card, string idempotencyKey,
        SdkException<CreatePaymentTokenError> oneStepFailure, CancellationToken cancellationToken)
    {
        try
        {
            var setupBody = new PPModels.SetupTokenRequest
            {
                PaymentSource = new PPModels.SetupTokenRequestPaymentSource
                {
                    Card = new PPModels.SetupTokenRequestCard
                    {
                        Name = card.CardholderName,
                        Number = card.Number,
                        Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                        SecurityCode = card.SecurityCode,
                        BillingAddress = ToAddress(card.BillingAddress)
                    }
                }
            };

            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: idempotencyKey,
                body: setupBody,
                ct: cancellationToken);

            var setupTokenId = setup.Id
                ?? throw ToPaymentException("The payment provider could not save the card.",
                    DebugIdOf(oneStepFailure.Error), oneStepFailure);

            var tokenBody = new PPModels.PaymentTokenRequest
            {
                PaymentSource = new PPModels.PaymentTokenRequestPaymentSource
                {
                    Token = new PPModels.VaultTokenRequest
                    {
                        Id = setupTokenId,
                        Type = PPEnums.VaultTokenRequestType.SetupToken
                    }
                }
            };

            var resp = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: tokenBody,
                ct: cancellationToken);

            return BuildVaultedCard(resp);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            throw ToPaymentException("The payment provider could not save the card.", DebugIdOf(ex.Error), ex);
        }
        // A CreatePaymentTokenError from the second step bubbles to VaultCardAsync's outer catch.
    }

    // ----------------------------------------------------------------- DeleteVaultedCardAsync

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw ToPaymentException("The payment provider could not delete the saved card.", DebugIdOf(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
    }

    // ----------------------------------------------------------------- request/response mapping

    private static PPModels.OrderRequest BuildCreateOrderBody(Money amount) => new()
    {
        Intent = PPEnums.CheckoutPaymentIntent.Capture,
        PurchaseUnits = new[]
        {
            new PPModels.PurchaseUnitRequest
            {
                Amount = new PPModels.AmountWithBreakdown
                {
                    CurrencyCode = amount.CurrencyCode,
                    Value = amount.Amount.ToString("0.00", CultureInfo.InvariantCulture)
                }
            }
        }
    };

    private static PPModels.Address? ToAddress(BillingAddress? billing)
    {
        if (billing is null)
        {
            return null;
        }

        return new PPModels.Address
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea1 = billing.State,
            AdminArea2 = billing.City,
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode
        };
    }

    private static PaymentAuthorization BuildAuthorization(string orderId, PPModels.Order captured, CardDetails? inputCard)
    {
        var captureId = ReadCaptureId(captured)
            ?? throw new PaymentGatewayException("The payment provider captured the payment but returned no capture id.");

        var display = BuildDisplay(captured.PaymentSource?.Card, inputCard);
        return new PaymentAuthorization(orderId, captureId, display);
    }

    private static string? ReadCaptureId(PPModels.Order order)
    {
        foreach (var unit in order.PurchaseUnits ?? Enumerable.Empty<PPModels.PurchaseUnit>())
        {
            foreach (var capture in unit.Payments?.Captures ?? Enumerable.Empty<PPModels.OrdersCapture>())
            {
                if (!string.IsNullOrEmpty(capture.Id))
                {
                    return capture.Id;
                }
            }
        }

        return null;
    }

    private static CardDisplay BuildDisplay(PPModels.CardResponse? responseCard, CardDetails? inputCard)
    {
        if (responseCard is not null)
        {
            var (month, year) = ParseExpiry(responseCard.Expiry);
            return new CardDisplay(
                responseCard.Brand?.Value,
                responseCard.LastDigits ?? Last4(inputCard?.Number),
                month ?? inputCard?.ExpiryMonth,
                year ?? inputCard?.ExpiryYear);
        }

        return new CardDisplay(null, Last4(inputCard?.Number), inputCard?.ExpiryMonth, inputCard?.ExpiryYear);
    }

    private static VaultedCard BuildVaultedCard(PPModels.PaymentTokenResponse response)
    {
        var vaultId = response.Id
            ?? throw new PaymentGatewayException("The payment provider did not return a vault token id.");

        var card = response.PaymentSource?.Card;   // CardPaymentTokenEntity? — never carries the full PAN
        var (month, year) = ParseExpiry(card?.Expiry);
        var display = new CardDisplay(card?.Brand?.Value, card?.LastDigits ?? string.Empty, month, year);
        return new VaultedCard(vaultId, display);
    }

    // ----------------------------------------------------------------- small helpers

    private static string FormatExpiry(int year, int month) =>
        $"{year:0000}-{month:00}";

    private static (int? Month, int? Year) ParseExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return (null, null);
        }

        var parts = expiry.Split('-');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
        {
            return (month, year);
        }

        return (null, null);
    }

    private static string Last4(string? pan)
    {
        if (string.IsNullOrEmpty(pan))
        {
            return string.Empty;
        }

        return pan.Length <= 4 ? pan : pan.Substring(pan.Length - 4);
    }

    // ----------------------------------------------------------------- error boundary helpers

    // debug_id is read via the concrete {Operation}Error's typed accessor (never a shared ApiError helper,
    // which would only see TryGetRawError). Orders/Payments -> TryGetError(out Error); Vault -> TryGetError1(out Error1).
    private static string? DebugIdOf(CreateOrderError e) => e.TryGetError(out var err) ? err.DebugId : null;
    private static string? DebugIdOf(CaptureOrderError e) => e.TryGetError(out var err) ? err.DebugId : null;
    private static string? DebugIdOf(RefundCapturedPaymentError e) => e.TryGetError(out var err) ? err.DebugId : null;
    private static string? DebugIdOf(CreatePaymentTokenError e) => e.TryGetError1(out var err) ? err.DebugId : null;
    private static string? DebugIdOf(CreateSetupTokenError e) => e.TryGetError1(out var err) ? err.DebugId : null;
    private static string? DebugIdOf(DeletePaymentTokenError e) => e.TryGetError1(out var err) ? err.DebugId : null;

    // Only a readable 5xx should skip the vault fallback; a typed 4xx (or an unreadable status) may be
    // "raw card source not accepted", so we still attempt the two-step flow.
    private static bool IsServerError(CreatePaymentTokenError e) =>
        e.TryGetRawError(out var raw) && (int)raw.StatusCode >= 500;

    private PaymentGatewayException ToPaymentException(string safeMessage, string? debugId, Exception inner)
    {
        // Log the provider failure server-side for diagnosis. The inner SdkException's detail is PayPal's
        // own error response (no card data). Never logs card details.
        _logger.LogWarning(inner, "PayPal call failed: {Message} (debug_id={DebugId}) detail={Detail}",
            safeMessage, debugId ?? "(none)", RawDetailOf(inner));
        return new(safeMessage, debugId, inner);
    }

    private PaymentGatewayException UnreadableResponse(JsonException inner)
    {
        _logger.LogWarning(inner, "PayPal returned an unreadable response.");
        return new("The payment provider returned an unreadable response.", null, inner);
    }

    private PaymentGatewayException Unreachable(Exception inner)
    {
        _logger.LogWarning(inner, "PayPal is currently unreachable.");
        return new("The payment provider is currently unreachable.", null, inner);
    }

    // Best-effort extraction of PayPal's raw error body (status + JSON) from a typed SdkException, for
    // server-side diagnosis. Returns null when the exception carries no readable raw error.
    private static string? RawDetailOf(Exception inner)
    {
        var error = inner.GetType().GetProperty("Error")?.GetValue(inner);
        if (error is null)
        {
            return null;
        }

        // Typed 4xx: the structured Error/Error1 is exposed via TryGetError / TryGetError1.
        foreach (var accessor in new[] { "TryGetError", "TryGetError1" })
        {
            var m = error.GetType().GetMethod(accessor);
            if (m is null)
            {
                continue;
            }

            var args = new object?[] { null };
            if (m.Invoke(error, args) is true && args[0] is not null)
            {
                try { return JsonSerializer.Serialize(args[0]); }
                catch { return args[0]!.ToString(); }
            }
        }

        // Fallback: the raw error body (status + JSON).
        var tryGetRaw = error.GetType().GetMethod("TryGetRawError");
        if (tryGetRaw is not null)
        {
            var args = new object?[] { null };
            if (tryGetRaw.Invoke(error, args) is true && args[0] is RawError raw)
            {
                var body = raw.ReadAsString();
                if (body is { Length: > 800 })
                {
                    body = body.Substring(0, 800);
                }
                return $"HTTP {(int)raw.StatusCode} {body}";
            }
        }

        return null;
    }

    // A transport failure (or the SDK's own timeout) — but NOT a cancellation the caller requested.
    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
        && !ct.IsCancellationRequested;
}
