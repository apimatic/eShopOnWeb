using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentService"/>, built entirely on the PayPal .NET SDK.
///
/// Design notes:
/// - Every write passes a stable <c>PayPal-Request-Id</c> (the caller's idempotency key, suffixed per
///   sub-call) so a retried or double-clicked request never charges or refunds twice at the provider.
/// - All SDK failures are translated at this single boundary into <see cref="PaymentException"/>:
///   a provider rejection (declined/invalid card) becomes a 4xx the caller can act on; an unreachable
///   or unreadable provider becomes a 5xx. Raw SDK/exception text is never propagated to callers.
/// - Full card details flow through here transiently and are never persisted or logged.
/// </summary>
public class PayPalPaymentService : IPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly TimeSpan _callBudget;

    public PayPalPaymentService(PayPalServerSdkClient client, PayPalOptions options)
    {
        _client = client;
        _callBudget = TimeSpan.FromSeconds(options.CallTimeoutSeconds > 0 ? options.CallTimeoutSeconds : 30);
    }

    public Task<CardPaymentResult> ChargeOrderWithCardAsync(PaymentAmount amount, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildBillingAddress(card)
        };
        return ChargeAsync(amount, cardRequest, idempotencyKey, cancellationToken);
    }

    public Task<CardPaymentResult> ChargeOrderWithVaultedCardAsync(PaymentAmount amount, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Reuse a saved card by referencing its vault token — no PAN/CVC is supplied.
        var cardRequest = new CardRequest { VaultId = vaultId };
        return ChargeAsync(amount, cardRequest, idempotencyKey, cancellationToken);
    }

    private async Task<CardPaymentResult> ChargeAsync(PaymentAmount amount, CardRequest cardRequest, string idempotencyKey, CancellationToken cancellationToken)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Capture,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = amount.Currency,
                        Value = FormatAmount(amount.Value)
                    }
                }
            }
        };

        // 1) Create the PayPal order (idempotent on the create request-id). The card is supplied at
        //    capture time (below), not here — attaching it at create makes PayPal capture immediately,
        //    which would then collide with the explicit capture.
        Order created;
        try
        {
            created = await Bounded(ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + ":create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                ct: ct), cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateOrderError(ex.Error, "The payment could not be created.");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }

        var payPalOrderId = created.Id;
        if (string.IsNullOrEmpty(payPalOrderId))
        {
            throw new PaymentProviderUnavailableException("The payment provider returned a response that could not be processed.");
        }

        // 2) Capture the funds (idempotent on the capture request-id). Ask for the full
        //    representation so the response carries the capture id we need for refunds.
        Order captured;
        try
        {
            captured = await Bounded(ct => _client.Orders.CaptureOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + ":capture",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderCaptureRequest
                {
                    PaymentSource = new OrderCaptureRequestPaymentSource { Card = cardRequest }
                },
                prefer: "return=representation",
                ct: ct), cancellationToken);
        }
        catch (SdkException<CaptureOrderError> ex)
        {
            throw TranslateOrderError(ex.Error, "The payment could not be captured.");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }

        var captureId = ExtractCaptureId(captured);
        if (string.IsNullOrEmpty(captureId))
        {
            throw new PaymentProviderUnavailableException("The payment was processed but its confirmation could not be read.");
        }

        return new CardPaymentResult(payPalOrderId, captureId);
    }

    public async Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Refund refund;
        try
        {
            refund = await Bounded(ct => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct), cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefundError(ex.Error);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }

        var refundId = refund.Id;
        if (string.IsNullOrEmpty(refundId))
        {
            throw new PaymentProviderUnavailableException("The refund provider returned a response that could not be processed.");
        }

        return new RefundResult(refundId, refund.Status?.Value ?? "UNKNOWN");
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildBillingAddress(card)
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await Bounded(ct => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                ct: ct), cancellationToken);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex.Error, "The card could not be saved.");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }

        var vaultId = response.Id;
        if (string.IsNullOrEmpty(vaultId))
        {
            throw new PaymentProviderUnavailableException("The payment provider returned a response that could not be processed.");
        }

        // Prefer the provider's safe descriptor; fall back to the (transient) input if a field is absent.
        var entity = response.PaymentSource?.Card;
        var brand = entity?.Brand?.Value ?? "UNKNOWN";
        var lastFour = !string.IsNullOrEmpty(entity?.LastDigits) ? entity!.LastDigits! : LastFourOf(card.Number);
        var expiry = !string.IsNullOrEmpty(entity?.Expiry) ? entity!.Expiry! : card.Expiry;

        return new VaultedCard(vaultId, brand, lastFour, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Bounded(async ct =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
                return true;
            }, cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex.Error, "The saved card could not be removed.");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            throw Unavailable(ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Applies an overall call budget (linked to the caller's token) on top of per-attempt SDK timeouts.</summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_callBudget);
        return await call(cts.Token);
    }

    private static Address? BuildBillingAddress(CardDetails card)
    {
        // country_code is required on an Address; default to US for the sandbox test card when unspecified.
        var country = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode!;
        return new Address
        {
            CountryCode = country,
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode
        };
    }

    private static string FormatAmount(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string LastFourOf(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static string? ExtractCaptureId(Order order)
    {
        // Order.PurchaseUnits[].Payments.Captures[].Id — null-check every hop; PayPal payloads vary.
        var units = order.PurchaseUnits;
        if (units == null)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var captures = unit.Payments?.Captures;
            if (captures == null)
            {
                continue;
            }

            foreach (var capture in captures)
            {
                if (!string.IsNullOrEmpty(capture.Id))
                {
                    return capture.Id;
                }
            }
        }

        return null;
    }

    private static bool IsConnectionFailure(Exception ex)
        => ex is System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static PaymentProviderUnavailableException Unavailable(Exception ex)
        => new("The payment provider is currently unavailable. Please try again.", ex);

    private static PaymentProviderUnavailableException Unreadable(Exception ex)
        => new("The payment provider returned a response that could not be processed.", ex);

    private static PaymentException TranslateOrderError(CreateOrderError error, string fallback)
    {
        if (error.TryGetError(out Error? typed) && typed != null)
        {
            return ClassifyProviderError(typed, fallback);
        }

        if (error.TryGetRawError(out RawError? raw) && raw != null)
        {
            return FromRawError(raw, fallback);
        }

        return new PaymentDeclinedException(fallback);
    }

    private static PaymentException TranslateOrderError(CaptureOrderError error, string fallback)
    {
        if (error.TryGetError(out Error? typed) && typed != null)
        {
            return ClassifyProviderError(typed, fallback);
        }

        if (error.TryGetRawError(out RawError? raw) && raw != null)
        {
            return FromRawError(raw, fallback);
        }

        return new PaymentDeclinedException(fallback);
    }

    private static PaymentException TranslateRefundError(RefundCapturedPaymentError error)
    {
        const string fallback = "The refund could not be processed.";

        if (error.TryGetError(out Error? typed) && typed != null)
        {
            return ClassifyProviderError(typed, fallback);
        }

        if (error.TryGetNoContent(out RawError? noContent) && noContent != null)
        {
            return new PaymentProviderUnavailableException(fallback);
        }

        if (error.TryGetRawError(out RawError? raw) && raw != null)
        {
            return FromRawError(raw, fallback);
        }

        return new PaymentDeclinedException(fallback);
    }

    private static PaymentException TranslateVaultError(CreatePaymentTokenError error, string fallback)
    {
        // Vault operations expose the payload via TryGetError1 (not TryGetError).
        if (error.TryGetError1(out Error1? typed) && typed != null)
        {
            return ClassifyProviderError(typed.Name, typed.Message, DescribeDetails(typed.Details), fallback);
        }

        if (error.TryGetRawError(out RawError? raw) && raw != null)
        {
            return FromRawError(raw, fallback);
        }

        return new PaymentDeclinedException(fallback);
    }

    private static PaymentException TranslateVaultError(DeletePaymentTokenError error, string fallback)
    {
        if (error.TryGetError1(out Error1? typed) && typed != null)
        {
            return ClassifyProviderError(typed.Name, typed.Message, DescribeDetails(typed.Details), fallback);
        }

        if (error.TryGetRawError(out RawError? raw) && raw != null)
        {
            return FromRawError(raw, fallback);
        }

        return new PaymentDeclinedException(fallback);
    }

    private static PaymentException ClassifyProviderError(Error error, string fallback)
        => ClassifyProviderError(error.Name, error.Message, DescribeDetails(error.Details), fallback);

    private static PaymentException ClassifyProviderError(string? name, string? message, string? detail, string fallback)
    {
        // A structured provider error is a rejection the caller can act on (a decline / invalid card /
        // validation issue) EXCEPT for a couple of names that mean "our side is misconfigured / the
        // provider faulted" — those are not the shopper's fault, so surface them as 5xx.
        if (IsServerSideName(name))
        {
            return new PaymentProviderUnavailableException("The payment provider is currently unavailable. Please try again.");
        }

        var text = ComposeMessage(message, detail) ?? fallback;
        return new PaymentDeclinedException(text);
    }

    private static PaymentException FromRawError(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status >= 500)
        {
            return new PaymentProviderUnavailableException("The payment provider is currently unavailable. Please try again.");
        }

        return new PaymentDeclinedException(fallback);
    }

    private static bool IsServerSideName(string? name)
        => name is "INTERNAL_SERVER_ERROR" or "INTERNAL_SERVICE_ERROR" or "SERVICE_UNAVAILABLE"
            or "AUTHENTICATION_FAILURE" or "INVALID_TOKEN" or "RATE_LIMIT_REACHED";

    private static string? DescribeDetails(IReadOnlyList<ErrorDetails>? details)
    {
        var first = details?.FirstOrDefault();
        if (first == null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(first.Description) ? first.Description : first.Issue;
    }

    private static string? DescribeDetails(IReadOnlyList<ErrorDetails1>? details)
    {
        var first = details?.FirstOrDefault();
        if (first == null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(first.Description) ? first.Description : first.Issue;
    }

    private static string? ComposeMessage(string? message, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(message) && !string.IsNullOrWhiteSpace(detail))
        {
            return $"{message} ({detail})";
        }

        return !string.IsNullOrWhiteSpace(message) ? message : detail;
    }
}
