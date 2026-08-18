using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal payment processor, implemented over the APIMatic-generated PayPal .NET SDK. Translates every
/// provider failure into a domain payment exception and never leaks SDK types or card details.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency =>
        !string.IsNullOrWhiteSpace(_options.Currency)
            ? _options.Currency!
            : throw new PaymentGatewayException("PayPal currency is not configured (PayPal:Currency).", null);

    public async Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency, CardDetails? card,
        string? vaultId, string idempotencyKey, string? customId, CancellationToken cancellationToken)
    {
        var cardRequest = vaultId is not null
            ? new CardRequest { VaultId = vaultId }
            : new CardRequest
            {
                Number = card!.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = ToAddress(card.BillingAddress)
            };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = Format(amount) },
                    CustomId = customId
                }
            },
            PaymentSource = new PaymentSource { Card = cardRequest }
        };

        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{idempotencyKey}-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: cancellationToken);

            GuardNoChallenge(created.Status, "authorize");
            var payPalOrderId = created.Id
                ?? throw new PaymentGatewayException("authorize: PayPal did not return an order id.", null);

            // With a card supplied at create, PayPal authorizes the order synchronously, so the authorization
            // is already on the create response. Only fall back to an explicit AuthorizeOrder when it is not.
            var authorization = created.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

            if (authorization?.Id is null)
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: cancellationToken);

                GuardNoChallenge(authorized.Status, "authorize");
                authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }

            var authorizationId = authorization?.Id
                ?? throw new PaymentGatewayException("authorize: PayPal did not return an authorization.", null);

            return new PayPalAuthorization(
                payPalOrderId,
                authorizationId,
                authorization!.Status?.Value ?? "CREATED",
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate("authorize", ex.Error.TryGetError(out var e) ? e : null, TryRaw(ex.Error)); }
        catch (SdkException<AuthorizeOrderError> ex) { throw Translate("authorize", ex.Error.TryGetError(out var e) ? e : null, TryRaw(ex.Error)); }
        catch (JsonException ex) { throw Unreadable("authorize", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("authorize", ex); }
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken)
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
                ?? throw new PaymentGatewayException("capture: PayPal did not return a capture id.", null);
            var breakdown = captured.SellerReceivableBreakdown;

            return new PayPalCapture(
                captureId,
                captured.Status?.Value ?? "",
                ParseMoney(captured.Amount, "capture"),
                captured.Amount?.CurrencyCode ?? Currency,
                ParseMoneyOrNull(breakdown?.PaypalFee),
                ParseMoneyOrNull(breakdown?.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate("capture",
                ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetNoContent(out var nc) ? nc : TryRaw(ex.Error));
        }
        catch (JsonException ex) { throw Unreadable("capture", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("capture", ex); }
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            var reauthorized = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{authorizationId}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } },
                prefer: "return=representation",
                ct: cancellationToken);

            var newAuthorizationId = reauthorized.Id
                ?? throw new PaymentGatewayException("reauthorize: PayPal did not return an authorization.", null);

            return new PayPalAuthorization(
                string.Empty,
                newAuthorizationId,
                reauthorized.Status?.Value ?? "CREATED",
                ParseDate(reauthorized.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A hold that can no longer be renewed must say so in terms an operator can act on.
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var issue = typed?.Details is { Count: > 0 } ? typed.Details[0].Issue : null;
            var message = typed?.Message ?? "the authorization can no longer be renewed";
            throw new AuthorizationNotRenewableException(
                $"Authorization {authorizationId} could not be renewed: {message}" +
                (string.IsNullOrEmpty(issue) ? "." : $" ({issue})."));
        }
        catch (JsonException ex) { throw Unreadable("reauthorize", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("reauthorize", ex); }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("void",
                ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetNoContent(out var nc) ? nc : TryRaw(ex.Error));
        }
        catch (JsonException ex) { throw Unreadable("void", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("void", ex); }
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) } }
            : null;

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
                ?? throw new PaymentGatewayException("refund: PayPal did not return a refund id.", null);

            return new PayPalRefund(
                refundId,
                refund.Status?.Value ?? "",
                ParseMoneyOrNull(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate("refund",
                ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetNoContent(out var nc) ? nc : TryRaw(ex.Error));
        }
        catch (JsonException ex) { throw Unreadable("refund", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("refund", ex); }
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: cancellationToken);

            var vaultId = token.Id
                ?? throw new PaymentGatewayException("vault: PayPal did not return a vault id.", null);
            var entity = token.PaymentSource?.Card;

            return new PayPalVaultedCard(
                vaultId,
                entity?.Brand?.Value ?? "",
                entity?.LastDigits ?? "",
                entity?.Expiry ?? card.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw Translate("vault", ex.Error.TryGetError1(out var e) ? e : null, TryRaw(ex.Error));
        }
        catch (JsonException ex) { throw Unreadable("vault", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("vault", ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw Translate("delete-vault", ex.Error.TryGetError1(out var e) ? e : null, TryRaw(ex.Error));
        }
        catch (JsonException ex) { throw Unreadable("delete-vault", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("delete-vault", ex); }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>();

        try
        {
            // PayPal's Transaction Search bounds each query to a 31-day window, so walk the whole [from, to]
            // range in <=31-day slices and page through each slice to cover the entire range.
            var windowStart = from;
            while (windowStart < to)
            {
                var windowEnd = windowStart.AddDays(31);
                if (windowEnd > to) windowEnd = to;

                var startDate = FormatDate(windowStart);
                var endDate = FormatDate(windowEnd);

                var page = 1;
                int totalPages;
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

                    if (response.TransactionDetails is not null)
                    {
                        foreach (var detail in response.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            // Dedupe across adjacent window boundaries.
                            if (info?.TransactionId is not null && !seen.Add(info.TransactionId))
                                continue;

                            results.Add(new PayPalTransaction(
                                info?.TransactionId,
                                ParseMoneyOrNull(info?.TransactionAmount),
                                info?.TransactionAmount?.CurrencyCode,
                                info?.TransactionStatus,
                                ParseDate(info?.TransactionInitiationDate ?? info?.TransactionUpdatedDate),
                                info?.InvoiceId,
                                info?.CustomField));
                        }
                    }

                    totalPages = response.TotalPages ?? 1;
                    page++;
                }
                while (page <= totalPages);

                windowStart = windowEnd;
            }

            return results;
        }
        catch (SdkException<RawError> ex)
        {
            // Transaction search is the one operation with no typed error model (Case B).
            var status = (int)ex.Error.StatusCode;
            throw new PaymentGatewayException(
                $"reconciliation: PayPal transaction search returned HTTP {status}: {ex.Error.ReadAsString()}", status, ex);
        }
        catch (JsonException ex) { throw Unreadable("reconciliation", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("reconciliation", ex); }
    }

    // --- helpers ---

    private void GuardNoChallenge(OrderStatus? status, string op)
    {
        if (status == OrderStatus.PayerActionRequired)
            throw new PaymentChallengeRequiredException(
                $"{op}: PayPal requires the shopper to approve this payment in a browser (PAYER_ACTION_REQUIRED). " +
                "This flow only supports payments that complete without a browser approval step.");
    }

    private static Address? ToAddress(CardBillingAddress? a) =>
        a is null
            ? null
            : new Address
            {
                CountryCode = a.CountryCode,
                AddressLine1 = a.AddressLine1,
                AddressLine2 = a.AddressLine2,
                AdminArea2 = a.AdminArea2,
                AdminArea1 = a.AdminArea1,
                PostalCode = a.PostalCode
            };

    private static string Format(decimal amount) => amount.ToString("0.00", Inv);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, Inv, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;

    private static decimal ParseMoney(Money? money, string op)
    {
        if (money?.Value is null || !decimal.TryParse(money.Value, NumberStyles.Any, Inv, out var value))
            throw new PaymentGatewayException($"{op}: PayPal returned an unreadable amount.", null);
        return value;
    }

    private static decimal? ParseMoneyOrNull(Money? money) =>
        money?.Value is string v && decimal.TryParse(v, NumberStyles.Any, Inv, out var value) ? value : null;

    private static RawError? TryRaw(ApiError error) => error.TryGetRawError(out var raw) ? raw : null;

    private PaymentGatewayException Translate(string op, Error? typed, RawError? raw)
    {
        if (typed is not null)
        {
            var message = typed.Message ?? $"{op} was rejected by PayPal.";
            var issues = typed.Details is { Count: > 0 }
                ? string.Join(", ", typed.Details.Select(d => d.Issue).Where(i => !string.IsNullOrEmpty(i)))
                : null;
            var full = string.IsNullOrEmpty(issues) ? message : $"{message} ({issues})";
            _logger.LogWarning("PayPal {0} rejected: {1}", op, full);
            // A typed error is PayPal rejecting our request — a deterministic client-actionable 4xx.
            return new PaymentGatewayException($"{op}: {full}", 422);
        }

        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            _logger.LogWarning("PayPal {0} failed with HTTP {1}", op, status);
            return new PaymentGatewayException($"{op}: PayPal returned HTTP {status}.", status);
        }

        return new PaymentGatewayException($"{op} failed at PayPal.", null);
    }

    private PaymentGatewayException Translate(string op, Error1? typed, RawError? raw)
    {
        if (typed is not null)
        {
            var message = typed.Message ?? $"{op} was rejected by PayPal.";
            _logger.LogWarning("PayPal {0} rejected: {1}", op, message);
            return new PaymentGatewayException($"{op}: {message}", 422);
        }

        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            return new PaymentGatewayException($"{op}: PayPal returned HTTP {status}.", status);
        }

        return new PaymentGatewayException($"{op} failed at PayPal.", null);
    }

    private PaymentGatewayException Unreadable(string op, Exception inner)
    {
        _logger.LogWarning("PayPal {0} returned an unreadable response.", op);
        return new PaymentGatewayException($"{op}: PayPal returned a response that could not be processed.", 502, inner);
    }

    private PaymentGatewayException Unreachable(string op, Exception inner)
    {
        _logger.LogWarning("PayPal {0} was unreachable.", op);
        return new PaymentGatewayException($"{op}: PayPal could not be reached. Please retry.", 502, inner);
    }
}
