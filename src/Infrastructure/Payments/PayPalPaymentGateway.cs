using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The PayPal implementation of <see cref="IPaymentGateway"/>. Every PayPal interaction goes
/// through the PayPal .NET SDK client here, and PayPal SDK types are mapped onto the app's own
/// domain types so nothing SDK-shaped leaks past this boundary. Provider failures (typed errors,
/// raw errors, unreadable bodies, transport failures) are all translated into
/// <see cref="PaymentGatewayException"/> with a caller-safe message.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "JPY", "KRW", "VND", "CLP", "PYG", "XAF", "XOF", "HUF", "TWD", "UGX", "RWF", "DJF", "GNF", "KMF", "VUV", "XPF" };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "BHD", "KWD", "OMR", "TND", "IQD", "JOD", "LYD" };

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    // ---- Authorize (hold) ---------------------------------------------------------------------

    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, CardDetails? card,
        string? vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currencyCode,
                        Value = FormatAmount(amount, currencyCode)
                    }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = vaultId is not null
                    ? new CardRequest { VaultId = vaultId }
                    : new CardRequest
                    {
                        Number = card!.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        Name = card.CardholderName
                    }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);

            var status = order.Status?.Value;
            if (status == "PAYER_ACTION_REQUIRED")
                throw ChallengeRequired();

            var payPalOrderId = order.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id for the authorization.");

            var auth = order.PurchaseUnits?
                .Select(pu => pu.Payments?.Authorizations?.FirstOrDefault())
                .FirstOrDefault(a => a is not null);

            if (auth is not null && auth.Id is not null)
                return new AuthorizationResult(payPalOrderId, auth.Id, auth.Status?.Value ?? "CREATED",
                    ParseDate(auth.ExpirationTime));

            // The order was created but not yet authorized inline — complete the authorization.
            if (status == "APPROVED" || status == "CREATED" || status == "SAVED")
            {
                var authResp = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-a",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: cancellationToken);

                if (authResp.Status?.Value == "PAYER_ACTION_REQUIRED")
                    throw ChallengeRequired();

                var auth2 = authResp.PurchaseUnits?
                    .Select(pu => pu.Payments?.Authorizations?.FirstOrDefault())
                    .FirstOrDefault(a => a is not null);

                if (auth2 is not null && auth2.Id is not null)
                    return new AuthorizationResult(payPalOrderId, auth2.Id, auth2.Status?.Value ?? "CREATED",
                        ParseDate(auth2.ExpirationTime));
            }

            throw new PaymentGatewayException(
                "PayPal accepted the order but did not return an authorization; the payment could not be held.", 422);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Capture ------------------------------------------------------------------------------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);

            var captureId = captured.Id
                ?? throw new PaymentGatewayException("PayPal did not return a capture id.");

            var breakdown = captured.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(captured.Amount) ?? 0m;
            var currency = breakdown?.GrossAmount?.CurrencyCode ?? captured.Amount?.CurrencyCode ?? "";

            return new CaptureResult(
                captureId,
                captured.Status?.Value ?? "COMPLETED",
                gross,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Inspect authorization ----------------------------------------------------------------

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: cancellationToken);

            return new AuthorizationState(auth.Status?.Value ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Re-authorize -------------------------------------------------------------------------

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);

            var newId = reauth.Id
                ?? throw new PaymentGatewayException("PayPal did not return a renewed authorization id.");

            return new AuthorizationResult(string.Empty, newId, reauth.Status?.Value ?? "CREATED",
                ParseDate(reauth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Void ---------------------------------------------------------------------------------

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"void-{authorizationId}",
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException)
        {
            // A successful void returns an empty (204) body, which the SDK cannot deserialize.
            // The success signal for a void is the 2xx status, not a body — a genuine failure
            // surfaces above as a typed VoidPaymentError — so an empty-body parse failure here
            // means the hold was released. Treat it as success.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Refund -------------------------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value, currencyCode) }
            };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);

            var refundId = refund.Id
                ?? throw new PaymentGatewayException("PayPal did not return a refund id.");

            return new RefundResult(
                refundId,
                refund.Status?.Value ?? "PENDING",
                ParseMoney(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currencyCode);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate(err);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Vault a card -------------------------------------------------------------------------

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId,
        string merchantCustomerId, CancellationToken cancellationToken = default)
    {
        var customer = existingCustomerId is not null
            ? new Customer { Id = existingCustomerId }
            : new Customer { MerchantCustomerId = merchantCustomerId };

        var body = new PaymentTokenRequest
        {
            Customer = customer,
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"vault-{ShortHash(card.Number)}",
                body: body,
                ct: cancellationToken);

            var tokenId = token.Id
                ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");

            var vaultedCard = token.PaymentSource?.Card;
            return new VaultedCard(
                tokenId,
                token.Customer?.Id,
                vaultedCard?.Brand?.Value,
                vaultedCard?.LastDigits,
                vaultedCard?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err)) throw Translate(err);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Delete a vaulted card ----------------------------------------------------------------

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: tokenId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err)) throw Translate(err);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw);
            throw Unknown();
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Transaction search (paginated, Case B) -----------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        var startDate = FormatPayPalDate(from);
        var endDate = FormatPayPalDate(to);

        var page = 1;
        var totalPages = 1;

        try
        {
            do
            {
                var response = await _client.TransactionSearch.SearchTransactions(
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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: cancellationToken);

                totalPages = response.TotalPages ?? 1;

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null)
                            continue;

                        results.Add(new PayPalTransaction(
                            info.TransactionId,
                            info.TransactionStatus,
                            ParseMoney(info.TransactionAmount),
                            info.TransactionAmount?.CurrencyCode,
                            Date: null));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            return results;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---- Error translation --------------------------------------------------------------------

    private static PaymentGatewayException Translate(Error error)
    {
        var message = FirstNonEmpty(error.Message, error.Name) ?? "The payment was declined by PayPal.";
        return new PaymentGatewayException(message, statusCode: 422, debugId: error.DebugId);
    }

    private static PaymentGatewayException Translate(Error1 error)
    {
        var message = FirstNonEmpty(error.Message, error.Name) ?? "PayPal could not process the card.";
        return new PaymentGatewayException(message, statusCode: 422, debugId: error.DebugId);
    }

    private static PaymentGatewayException FromRaw(RawError raw) =>
        new($"PayPal request failed with HTTP status {(int)raw.StatusCode}.", statusCode: (int)raw.StatusCode);

    private static PaymentGatewayException Unreadable(JsonException ex) =>
        new("PayPal returned a response that could not be processed.", innerException: ex);

    private static PaymentGatewayException Unreachable(Exception ex) =>
        new("The payment provider is currently unreachable.", innerException: ex);

    private static PaymentGatewayException Unknown() =>
        new("The payment provider returned an unrecognised error.");

    private static PaymentChallengeRequiredException ChallengeRequired() =>
        new("PayPal requires the shopper to approve this payment in a browser (a challenge such as 3-D Secure), which this integration does not support.");

    // ---- Formatting helpers -------------------------------------------------------------------

    private static string FormatAmount(decimal amount, string currencyCode)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currencyCode) ? 0
            : ThreeDecimalCurrencies.Contains(currencyCode) ? 3
            : 2;
        var rounded = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null)
            return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string ShortHash(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
