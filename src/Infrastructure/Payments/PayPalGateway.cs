using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
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
/// PayPal implementation of <see cref="IPayPalGateway"/>. This is the only type in the solution that
/// references the PayPal SDK; it translates each domain operation into an SDK call and maps SDK results
/// (and errors) onto the <see cref="Microsoft.eShopWeb.ApplicationCore.Payments"/> DTOs and exceptions.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;

    // Best-effort issue-code signals used to distinguish state failures the caller can recover from
    // (reauthorize / place-again) from generic PayPal rejections. The exact wire strings PayPal returns
    // are [UNVERIFIED] (not part of the SDK surface); on no match we fall back to PayPalApiException.
    private static readonly string[] NotCapturableSignals =
    {
        "AUTHORIZATION_ALREADY_CAPTURED", "ALREADY_CAPTURED", "PREVIOUSLY_CAPTURED",
        "AUTHORIZATION_EXPIRED", "EXPIRED", "AUTHORIZATION_VOIDED", "VOIDED",
        "MAX_CAPTURE_COUNT_EXCEEDED", "PAYMENT_ALREADY_DONE", "INVALID_AUTHORIZATION_STATE",
        "AUTH_CAPTURE_NOT_ALLOWED"
    };

    private static readonly string[] NotReauthorizableSignals =
    {
        "AUTHORIZATION_EXPIRED", "EXPIRED", "REAUTHORIZATION_NOT_ALLOWED",
        "CANNOT_BE_REAUTHORIZED", "REAUTHORIZATION_TOO_LATE", "HONOR_PERIOD"
    };

    public PayPalGateway(PayPalServerSdkClient client, PayPalOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken cancellationToken = default)
    {
        if (instruction is null) throw new ArgumentNullException(nameof(instruction));

        CardRequest card;
        if (!string.IsNullOrEmpty(instruction.VaultId))
        {
            // Pay with a previously-vaulted card: only the vault id is sent, never raw card data.
            card = new CardRequest { VaultId = instruction.VaultId };
        }
        else if (instruction.Card is not null)
        {
            var c = instruction.Card;
            card = new CardRequest
            {
                Name = c.CardholderName,
                Number = c.Number,
                Expiry = c.Expiry,
                SecurityCode = c.SecurityCode,
                BillingAddress = new Address
                {
                    CountryCode = c.BillingCountryCode ?? "US",
                    AddressLine1 = c.BillingAddressLine,
                    AdminArea2 = c.BillingCity,
                    AdminArea1 = c.BillingState,
                    PostalCode = c.BillingPostalCode
                }
            };
        }
        else
        {
            throw new PaymentValidationException("An authorization requires either a card or a vault id.");
        }

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = instruction.CurrencyCode,
                        Value = FormatAmount(instruction.Amount)
                    },
                    CustomId = instruction.CustomId,
                    InvoiceId = instruction.InvoiceId
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: instruction.IdempotencyKey.ToString(),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw ToApiException(err, ex);
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal order creation failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        var orderId = order.Id ?? throw new PayPalApiException("PayPal returned an order without an id.");

        // A challenge / payer-approval requirement is not supported by this server-side integration.
        var needsPayerAction =
            order.Status == OrderStatus.PayerActionRequired
            || (order.Links?.Any(l => l.Rel is "approve" or "payer-action") ?? false);
        if (needsPayerAction)
        {
            throw new PayPalApiException(
                "PayPal requires additional payer action (approval / 3-D Secure) for this order, which this integration does not support.");
        }

        var auth = FirstAuthorization(order.PurchaseUnits);
        if (auth is not null)
        {
            return new AuthorizationResult(
                orderId,
                auth.Id ?? throw new PayPalApiException("PayPal returned an authorization without an id."),
                Wire(auth.Status) ?? string.Empty,
                ParseTimestamp(auth.ExpirationTime));
        }

        // No authorization was produced at create time — run the explicit AuthorizeOrder step.
        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: instruction.IdempotencyKey.ToString(),
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw ToApiException(err, ex);
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal authorization failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        var producedAuth = FirstAuthorization(authorized.PurchaseUnits)
            ?? throw new PayPalApiException("PayPal did not return an authorization for the order.");

        return new AuthorizationResult(
            authorized.Id ?? orderId,
            producedAuth.Id ?? throw new PayPalApiException("PayPal returned an authorization without an id."),
            Wire(producedAuth.Status) ?? string.Empty,
            ParseTimestamp(producedAuth.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string currencyCode, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey.ToString(),
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
            {
                if (MatchesAny(CollectSignals(err), NotCapturableSignals))
                {
                    throw new AuthorizationNotCapturableException(DescribeError(err), ex);
                }
                throw ToApiException(err, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex, "PayPal capture failed.");
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal capture failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var capturedAmount = ParseAmount(capture.Amount?.Value ?? breakdown?.GrossAmount.Value);
        return new CaptureResult(
            capture.Id ?? string.Empty,
            Wire(capture.Status) ?? string.Empty,
            capturedAmount,
            ParseAmountOrNull(breakdown?.PaypalFee?.Value),
            ParseAmountOrNull(breakdown?.NetAmount?.Value),
            currencyCode);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) }
        };

        PaymentAuthorization authorization;
        try
        {
            authorization = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey.ToString(),
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
            {
                if (MatchesAny(CollectSignals(err), NotReauthorizableSignals))
                {
                    throw new AuthorizationNotReauthorizableException(DescribeError(err), JoinIssues(err), ex);
                }
                throw ToApiException(err, ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex, "PayPal reauthorization failed.");
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal reauthorization failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        // Reauthorize returns a PaymentAuthorization only — it carries no order id, so PayPalOrderId is empty.
        return new AuthorizationResult(
            string.Empty,
            authorization.Id ?? throw new PayPalApiException("PayPal returned a reauthorization without an id."),
            Wire(authorization.Status) ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<VoidResult> VoidAsync(string authorizationId, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        PaymentAuthorization? authorization = null;
        try
        {
            authorization = await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey.ToString(),
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw ToApiException(err, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex, "PayPal void failed.");
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal void failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        // The void response is frequently empty-bodied; default the status to VOIDED when nothing came back.
        var status = Wire(authorization?.Status);
        return new VoidResult(authorizationId, string.IsNullOrEmpty(status) ? Wire(AuthorizationStatus.Voided)! : status);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // null amount => full refund (empty body); a set amount => partial refund.
        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } };

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw ToApiException(err, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex, "PayPal refund failed.");
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal refund failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        var refundedAmount = ParseAmountOrNull(refund.Amount?.Value) ?? amount ?? 0m;
        return new RefundResult(
            refund.Id ?? string.Empty,
            Wire(refund.Status) ?? string.Empty,
            refundedAmount,
            refund.Amount?.CurrencyCode ?? currencyCode);
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        if (card is null) throw new ArgumentNullException(nameof(card));

        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = new Address
                    {
                        CountryCode = card.BillingCountryCode ?? "US",
                        AddressLine1 = card.BillingAddressLine,
                        AdminArea2 = card.BillingCity,
                        AdminArea1 = card.BillingState,
                        PostalCode = card.BillingPostalCode
                    }
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: Guid.NewGuid().ToString(),
                body: body,
                ct: cancellationToken);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            // Vault operations use the Error1 accessor (not TryGetError).
            if (ex.Error.TryGetError1(out var err)) throw ToApiException(err, ex);
            throw FromRawFallback(ex, ex.Error.TryGetRawError(out var raw) ? raw : null, "PayPal card vaulting failed.");
        }
        catch (JsonException ex)
        {
            throw ToUnprocessable(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }

        var cardEntity = response.PaymentSource?.Card;
        return new VaultedCardResult(
            response.Id ?? string.Empty,
            Wire(cardEntity?.Brand) ?? string.Empty,
            cardEntity?.LastDigits ?? string.Empty,
            cardEntity?.Expiry ?? string.Empty);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var startDate = ToIso8601(from);
        var endDate = ToIso8601(to);

        var results = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: startDate,
                    endDate: endDate,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    pageSize: 100,
                    page: page,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                // Case B: no typed accessors — read status and body straight off the RawError.
                var raw = ex.Error;
                throw new PayPalApiException(
                    $"PayPal transaction search failed with HTTP {(int)raw.StatusCode}.",
                    (int)raw.StatusCode,
                    SafeReadRaw(raw),
                    ex);
            }
            catch (JsonException ex)
            {
                throw ToUnprocessable(ex);
            }
            catch (HttpRequestException ex)
            {
                throw Unreachable(ex);
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new PayPalTransaction(
                        info.TransactionId ?? string.Empty,
                        info.TransactionStatus,
                        ParseAmountOrNull(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmountOrNull(info.FeeAmount?.Value),
                        info.InvoiceId,
                        info.CustomField,
                        ParseTimestamp(info.TransactionInitiationDate)));
                }
            }

            // An empty result for a recent range is normal (reporting lag): TotalPages is 0/null and we stop.
            totalPages = response.TotalPages ?? 0;
            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units)
    {
        if (units is null) return null;
        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is null) continue;
            foreach (var authorization in authorizations)
            {
                if (!string.IsNullOrEmpty(authorization.Id)) return authorization;
            }
        }
        return null;
    }

    // The SDK's enums are `StringEnum<T>` records: their record ToString() prints "CardBrand { Value = VISA }",
    // so read the raw wire value ("VISA") via the documented implicit string conversion, not ToString().
    private static string? Wire(CardBrand? value) => value is null ? null : (string)value;
    private static string? Wire(AuthorizationStatus? value) => value is null ? null : (string)value;
    private static string? Wire(CaptureStatus? value) => value is null ? null : (string)value;
    private static string? Wire(RefundStatus? value) => value is null ? null : (string)value;

    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? ParseAmountOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string ToIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool MatchesAny(string text, string[] signals) =>
        signals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string CollectSignals(Error error)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(error.Message)) parts.Add(error.Message);
        if (error.Details is not null)
        {
            foreach (var detail in error.Details)
            {
                parts.Add(detail.Issue);
                if (!string.IsNullOrEmpty(detail.Description)) parts.Add(detail.Description!);
            }
        }
        return string.Join(" | ", parts);
    }

    private static string JoinIssues(Error error)
    {
        if (error.Details is null || error.Details.Count == 0) return string.Empty;
        return string.Join("; ", error.Details.Select(d =>
            string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
    }

    private static string DescribeError(Error error)
    {
        var issues = JoinIssues(error);
        return string.IsNullOrEmpty(issues) ? error.Message : $"{error.Message} ({issues})";
    }

    private static string JoinIssues(Error1 error)
    {
        if (error.Details is null || error.Details.Count == 0) return string.Empty;
        return string.Join("; ", error.Details.Select(d =>
            string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
    }

    private static string DescribeError(Error1 error)
    {
        var issues = JoinIssues(error);
        return string.IsNullOrEmpty(issues) ? error.Message : $"{error.Message} ({issues})";
    }

    // PayPal's typed 4xx error model (Error / Error1) carries no HTTP status code, so PayPalStatusCode is
    // left null here; the operator-facing detail is the joined issue list.
    private static PayPalApiException ToApiException(Error error, Exception inner) =>
        new PayPalApiException(DescribeError(error), null, JoinIssues(error), inner);

    private static PayPalApiException ToApiException(Error1 error, Exception inner) =>
        new PayPalApiException(DescribeError(error), null, JoinIssues(error), inner);

    private static PayPalApiException FromRaw(RawError raw, Exception inner, string fallbackMessage) =>
        new PayPalApiException(
            $"{fallbackMessage} PayPal returned HTTP {(int)raw.StatusCode}.",
            (int)raw.StatusCode,
            SafeReadRaw(raw),
            inner);

    private static PayPalApiException FromRawFallback(Exception inner, RawError? raw, string fallbackMessage) =>
        raw is null
            ? new PayPalApiException(fallbackMessage, null, null, inner)
            : FromRaw(raw, inner, fallbackMessage);

    // A JsonException is a drifted body — a deterministic rejection (the error body did not match the
    // generated error shape) or a broken 2xx. Surface it WITHOUT inventing an HTTP status.
    private static PayPalApiException ToUnprocessable(JsonException inner) =>
        new PayPalApiException("PayPal returned a response that could not be processed.", null, null, inner);

    private static PayPalApiException Unreachable(HttpRequestException inner) =>
        new PayPalApiException("PayPal is currently unreachable.", null, null, inner);

    private static string? SafeReadRaw(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return null;
        }
    }
}
