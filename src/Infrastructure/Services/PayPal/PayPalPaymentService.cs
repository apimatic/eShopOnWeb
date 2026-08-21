using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The only place the PayPal SDK is referenced. Translates eShop payment operations into SDK calls, converts
/// every SDK failure into a single <see cref="PayPalException"/> carrying caller-safe detail, and formats all
/// amounts to the cent. Idempotency keys are sent to PayPal as the request id so a retry never double-charges.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentService> _logger;
    private readonly string _currency;

    public PayPalPaymentService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        IAppLogger<PayPalPaymentService> logger)
    {
        _client = client;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency!.Trim();
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, PayPalCard card, string idempotencyKey, CancellationToken ct)
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
                        CurrencyCode = _currency,
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(card) }
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
                ct: ct);

            var authorization = order.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

            if (authorization is not null && !string.IsNullOrEmpty(authorization.Id))
            {
                return new PayPalAuthorizationResult
                {
                    PayPalOrderId = order.Id,
                    AuthorizationId = authorization.Id,
                    AuthorizationStatus = authorization.Status?.Value,
                    ExpiresAt = null
                };
            }

            // No straight-through authorization: PayPal wants shopper approval (e.g. a 3DS challenge). We do
            // not build an approval round-trip — we stop and report the raw status for an operator to act on.
            var statusValue = order.Status?.Value ?? "UNKNOWN";
            _logger.LogWarning("PayPal order {0} needs shopper approval (status {1}); stopping.", order.Id ?? "?", statusValue);
            return new PayPalAuthorizationResult
            {
                PayPalOrderId = order.Id,
                RequiresApproval = true,
                ApprovalDetail = $"PayPal returned status '{statusValue}', which requires shopper approval in a browser."
            };
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TranslateOrderError(error, "authorize the order", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "authorize the order", ex);
            }
            throw new PayPalException("PayPal could not authorize the order.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "authorize the order");
        }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
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
                ct: ct);

            var breakdown = captured.SellerReceivableBreakdown;
            var grossMoney = breakdown?.GrossAmount ?? captured.Amount;

            return new PayPalCaptureResult
            {
                CaptureId = captured.Id ?? string.Empty,
                Status = captured.Status?.Value ?? "UNKNOWN",
                CapturedAmount = ParseMoney(grossMoney) ?? 0m,
                PayPalFee = ParseMoney(breakdown?.PaypalFee),
                NetAmount = ParseMoney(breakdown?.NetAmount),
                CurrencyCode = grossMoney?.CurrencyCode
            };
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TranslateOrderError(error, "capture the payment", ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw TranslateRaw(noContent, "capture the payment", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "capture the payment", ex);
            }
            throw new PayPalException("PayPal could not capture the payment.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "capture the payment");
        }
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = _currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: ct);

            return new PayPalAuthorizationResult
            {
                AuthorizationId = reauth.Id,
                AuthorizationStatus = reauth.Status?.Value,
                ExpiresAt = TryParseDate(reauth.ExpirationTime?.ToString())
            };
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TranslateOrderError(error, "renew the authorization", ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw TranslateRaw(noContent, "renew the authorization", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "renew the authorization", ex);
            }
            throw new PayPalException("PayPal could not renew the authorization.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "renew the authorization");
        }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TranslateOrderError(error, "cancel the authorization", ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw TranslateRaw(noContent, "cancel the authorization", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "cancel the authorization", ex);
            }
            throw new PayPalException("PayPal could not cancel the authorization.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "cancel the authorization");
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = _currency, Value = FormatAmount(amount.Value) } }
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
                ct: ct);

            return new PayPalRefundResult
            {
                RefundId = refund.Id ?? string.Empty,
                Status = refund.Status?.Value ?? "UNKNOWN",
                Amount = ParseMoney(refund.Amount),
                TotalRefunded = ParseMoney(refund.SellerPayableBreakdown?.TotalRefundedAmount),
                CurrencyCode = refund.Amount?.CurrencyCode
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw TranslateOrderError(error, "refund the payment", ex);
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw TranslateRaw(noContent, "refund the payment", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "refund the payment", ex);
            }
            throw new PayPalException("PayPal could not refund the payment.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "refund the payment");
        }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCard card, string idempotencyKey, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = NormalizeExpiry(card.Expiry),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: ct);

            var savedCard = token.PaymentSource?.Card;
            return new PayPalVaultResult
            {
                VaultId = token.Id ?? string.Empty,
                CardBrand = savedCard?.Brand?.Value,
                LastFourDigits = savedCard?.LastDigits ?? DeriveLastFour(card.Number),
                Expiry = savedCard?.Expiry ?? NormalizeExpiry(card.Expiry),
                CardholderName = savedCard?.Name ?? card.Name
            };
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            // The typed body (Error1) is a card rejection; do not surface its raw content. Untyped statuses
            // fall through to the RawError branch.
            if (ex.Error.TryGetError1(out _))
            {
                throw new PayPalException("PayPal rejected the card details, so the card could not be saved.",
                    HttpStatusCode.UnprocessableEntity, isBusinessRule: true, inner: ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "save the card", ex);
            }
            throw new PayPalException("PayPal could not save the card.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "save the card");
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out _))
            {
                throw new PayPalException("PayPal could not remove the saved card.",
                    HttpStatusCode.BadRequest, isBusinessRule: true, inner: ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "remove the saved card", ex);
            }
            throw new PayPalException("PayPal could not remove the saved card.", inner: ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "remove the saved card");
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransactionRecord>();
        var startDate = FormatDate(from);
        var endDate = FormatDate(to);

        int page = 1;
        int totalPages = 1;

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
                    ct: ct);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        results.Add(new PayPalTransactionRecord
                        {
                            TransactionId = info?.TransactionId,
                            Amount = ParseMoney(info?.TransactionAmount),
                            CurrencyCode = info?.TransactionAmount?.CurrencyCode,
                            Status = info?.TransactionStatus,
                            Date = TryParseDate(info?.TransactionInitiationDate?.ToString())
                        });
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "search transactions", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw TranslateTransport(ex, "search transactions");
        }

        return results;
    }

    // --- SDK model building helpers ---

    private static CardRequest BuildCardRequest(PayPalCard card)
    {
        if (card.IsVaulted)
        {
            return new CardRequest { VaultId = card.VaultId };
        }

        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address? BuildAddress(PayPalBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode!
        };
    }

    // --- Error translation ---

    private PayPalException TranslateOrderError(Error error, string action, Exception inner)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var message = error.Message ?? error.Name ?? $"PayPal could not {action}.";
        var description = string.IsNullOrEmpty(issue) ? message : $"{message} ({issue})";
        _logger.LogWarning("PayPal declined to {0}: {1}", action, description);
        // A typed error is a deterministic 4xx rejection (validation/business), not an outage.
        return new PayPalException(description, HttpStatusCode.UnprocessableEntity, isBusinessRule: true, issue: issue, inner: inner);
    }

    private PayPalException TranslateRaw(RawError raw, string action, Exception inner)
    {
        var status = raw.StatusCode;
        var transient = (int)status >= 500;
        _logger.LogWarning("PayPal returned {0} while trying to {1}.", (int)status, action);
        return new PayPalException(
            $"PayPal returned an error ({(int)status}) while trying to {action}.",
            status,
            isBusinessRule: !transient,
            isTransient: transient,
            inner: inner);
    }

    private PayPalException TranslateTransport(Exception ex, string action)
    {
        if (ex is JsonException)
        {
            _logger.LogWarning("PayPal returned a response that could not be processed while trying to {0}.", action);
            return new PayPalException(
                $"PayPal returned a response that could not be processed while trying to {action}.",
                isTransient: true, inner: ex);
        }

        _logger.LogWarning("PayPal was unreachable while trying to {0}.", action);
        return new PayPalException($"PayPal was unreachable while trying to {action}.", isTransient: true, inner: ex);
    }

    private static bool IsTransportOrJson(Exception ex) =>
        ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException;

    // --- Value helpers ---

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }
        return decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : (decimal?)null;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
    }

    private static string DeriveLastFour(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return "0000";
        }
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    /// <summary>
    /// Normalizes a card expiry to PayPal's <c>YYYY-MM</c> wire format, accepting common shopper inputs
    /// (<c>YYYY-MM</c>, <c>MM/YY</c>, <c>MM/YYYY</c>, <c>MM-YYYY</c>).
    /// </summary>
    internal static string? NormalizeExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return expiry;
        }

        var trimmed = expiry.Trim();
        var parts = trimmed.Split(new[] { '/', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return trimmed;
        }

        // Already YYYY-MM
        if (parts[0].Length == 4 && int.TryParse(parts[0], out var year4) && int.TryParse(parts[1], out var month1))
        {
            return $"{year4:D4}-{month1:D2}";
        }

        // MM/YY or MM/YYYY
        if (int.TryParse(parts[0], out var month2) && int.TryParse(parts[1], out var year))
        {
            if (parts[1].Length == 2)
            {
                year += 2000;
            }
            return $"{year:D4}-{month2:D2}";
        }

        return trimmed;
    }
}
