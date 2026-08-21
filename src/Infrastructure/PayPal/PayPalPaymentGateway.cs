using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single boundary between the application and the PayPal .NET SDK. It builds the SDK request
/// models, reads the response models, and translates every SDK failure — typed API errors, raw
/// errors, malformed bodies and transport failures — into the application's own
/// <see cref="PaymentGatewayException"/> family with caller-safe messages. Card details flow in but
/// are never returned or logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, string idempotencyKey, CancellationToken ct)
    {
        CardRequest card = !string.IsNullOrEmpty(request.VaultId)
            ? new CardRequest { VaultId = request.VaultId }
            : BuildCard(request.Card!);

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PaymentSource = new PaymentSource { Card = card },
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatAmount(request.Amount)
                    },
                    CustomId = $"eshop-order-{request.OrderReference}"
                }
            }
        };

        using var cts = LinkedTimeout(ct);
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
                ct: cts.Token);

            GuardNoApproval(order.Status?.Value, order.Links?.Select(l => l.Rel));

            var auth = ExtractAuthorization(order.PurchaseUnits);
            var payPalOrderId = order.Id;

            if (auth is null)
            {
                // Direct card + AUTHORIZE normally yields the authorization on CreateOrder; if not,
                // fall back to the explicit authorize step.
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-auth",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: cts.Token);

                GuardNoApproval(authorized.Status?.Value, authorized.Links?.Select(l => l.Rel));
                auth = ExtractAuthorization(authorized.PurchaseUnits);
                payPalOrderId ??= authorized.Id;
            }

            if (auth is null || string.IsNullOrEmpty(auth.Id))
                throw new PaymentGatewayException("PayPal did not return an authorization for the card payment.", 502);

            return new AuthorizationResult(payPalOrderId!, auth.Id!, auth.Status?.Value);
        }
        catch (SdkException<CreateOrderError> ex) { throw TranslateCreateOrder(ex); }
        catch (SdkException<AuthorizeOrderError> ex) { throw TranslateAuthorizeOrder(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = LinkedTimeout(ct);
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cts.Token);

            var breakdown = captured.SellerReceivableBreakdown;
            var capturedAmount = ParseMoney(captured.Amount) ?? 0m;
            var currency = captured.Amount?.CurrencyCode ?? "";

            return new CaptureResult(
                CaptureId: captured.Id!,
                Status: captured.Status?.Value,
                CapturedAmount: capturedAmount,
                PayPalFee: ParseMoney(breakdown?.PaypalFee),
                NetAmount: ParseMoney(breakdown?.NetAmount),
                Currency: currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw TranslateCapture(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct)
    {
        using var cts = LinkedTimeout(ct);
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: cts.Token);

            return new ReauthorizationResult(reauth.Id!, reauth.Status?.Value);
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw TranslateReauthorize(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = LinkedTimeout(ct);
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                ct: cts.Token);
        }
        catch (SdkException<VoidPaymentError> ex) { throw TranslateVoid(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException)
        {
            // A successful void returns HTTP 204 with an empty body; the SDK throws while trying to
            // deserialize that empty body into a PaymentAuthorization. A genuine void failure throws
            // SdkException<VoidPaymentError> above (handled), so reaching here means the hold was
            // released successfully — treat it as success.
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } };

        using var cts = LinkedTimeout(ct);
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cts.Token);

            var refundedAmount = ParseMoney(refund.Amount) ?? amount ?? 0m;
            return new RefundResult(refund.Id!, refund.Status?.Value, refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw TranslateRefund(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    public async Task<VaultCardResult> VaultCardAsync(PayPalCardData card, CancellationToken ct)
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
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        using var cts = LinkedTimeout(ct);
        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: cts.Token);

            GuardNoApproval(status: null, token.Links?.Select(l => l.Rel));

            var entity = token.PaymentSource?.Card;
            return new VaultCardResult(
                VaultId: token.Id!,
                Brand: entity?.Brand?.Value,
                LastDigits: entity?.LastDigits,
                Expiry: entity?.Expiry,
                CardholderName: entity?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw TranslateCreatePaymentToken(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var cts = LinkedTimeout(ct);
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw TranslateDeletePaymentToken(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    // PayPal's Transaction Search accepts at most a 31-day range per request, so a wider range is
    // walked in contiguous windows (kept safely under the limit) and the results concatenated.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(30);

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // De-duplicate by transaction id so a transaction landing on a window boundary is not counted twice.
        var byId = new Dictionary<string, ReconciliationTransaction>();
        var noId = new List<ReconciliationTransaction>();
        using var cts = LinkedTimeout(ct);
        try
        {
            var windowStart = from;
            while (windowStart < to)
            {
                var windowEnd = windowStart + MaxSearchWindow;
                if (windowEnd > to) windowEnd = to;

                int page = 1;
                int totalPages;
                do
                {
                    var resp = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatRfc3339(windowStart),
                        endDate: FormatRfc3339(windowEnd),
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
                        ct: cts.Token);

                    totalPages = resp.TotalPages ?? 1;

                    if (resp.TransactionDetails is not null)
                    {
                        foreach (var detail in resp.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            if (info is null) continue;

                            var record = new ReconciliationTransaction(
                                TransactionId: info.TransactionId,
                                Status: info.TransactionStatus,
                                Amount: ParseMoney(info.TransactionAmount),
                                Currency: info.TransactionAmount?.CurrencyCode,
                                Fee: ParseMoney(info.FeeAmount),
                                InitiatedDate: ParseDate(info.TransactionInitiationDate),
                                UpdatedDate: ParseDate(info.TransactionUpdatedDate),
                                InvoiceId: info.InvoiceId,
                                CustomField: info.CustomField);

                            if (string.IsNullOrEmpty(record.TransactionId))
                                noId.Add(record);
                            else
                                byId[record.TransactionId] = record;
                        }
                    }

                    page++;
                }
                while (page <= totalPages);

                windowStart = windowEnd;
            }

            var all = new List<ReconciliationTransaction>(byId.Values);
            all.AddRange(noId);
            return all;
        }
        catch (SdkException<RawError> ex)
        {
            throw new PaymentGatewayException(
                $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).",
                (int)ex.Error.StatusCode);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Malformed(ex); }
    }

    // ---- request/response helpers ----------------------------------------------------------

    private static CardRequest BuildCard(PayPalCardData c) => new CardRequest
    {
        Number = c.Number,
        Expiry = c.Expiry,
        SecurityCode = c.SecurityCode,
        Name = c.CardholderName,
        BillingAddress = MapAddress(c.BillingAddress)
    };

    private static Address? MapAddress(PayPalBillingAddress? a)
    {
        if (a is null) return null;
        return new Address
        {
            AddressLine1 = a.AddressLine1,
            AddressLine2 = a.AddressLine2,
            AdminArea2 = a.City,
            AdminArea1 = a.State,
            PostalCode = a.PostalCode,
            CountryCode = a.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? units)
    {
        if (units is null) return null;
        foreach (var unit in units)
        {
            var auths = unit.Payments?.Authorizations;
            if (auths is null) continue;
            foreach (var auth in auths)
                if (!string.IsNullOrEmpty(auth.Id)) return auth;
        }
        return null;
    }

    private static void GuardNoApproval(string? status, IEnumerable<string?>? linkRels)
    {
        bool payerAction =
            string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (linkRels?.Any(r => string.Equals(r, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

        if (payerAction)
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser. " +
                "This integration does not perform a browser approval round-trip.");
        }
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'.'fff'Z'", CultureInfo.InvariantCulture);

    private CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    // ---- error translation -----------------------------------------------------------------

    private static PaymentGatewayException TranslateCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error err))
        {
            var (message, issue) = Describe(err);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal rejected the order (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal rejected the order.", 502);
    }

    private static PaymentGatewayException TranslateAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error err))
        {
            var (message, issue) = Describe(err);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal could not authorize the order (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal could not authorize the order.", 502);
    }

    private static PaymentGatewayException TranslateCapture(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error err))
        {
            var (message, issue) = Describe(err);
            if (IsExpiredAuthorization(issue, message))
                return new AuthorizationExpiredException(message, issue);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetNoContent(out RawError nc))
            return new PaymentGatewayException($"PayPal could not capture the authorization (HTTP {(int)nc.StatusCode}).", (int)nc.StatusCode);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal could not capture the authorization (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal could not capture the authorization.", 502);
    }

    private static PaymentGatewayException TranslateReauthorize(SdkException<ReauthorizePaymentError> ex)
    {
        string message = "PayPal could not renew the authorization.";
        string? issue = null;
        if (ex.Error.TryGetError(out Error err))
        {
            (message, issue) = Describe(err);
        }
        else if (ex.Error.TryGetNoContent(out RawError nc))
        {
            message = $"PayPal could not renew the authorization (HTTP {(int)nc.StatusCode}).";
        }
        else if (ex.Error.TryGetRawError(out RawError raw))
        {
            message = $"PayPal could not renew the authorization (HTTP {(int)raw.StatusCode}).";
        }

        return new AuthorizationNotRenewableException(
            "The authorization for this order can no longer be renewed, so the order cannot be fulfilled as-is; " +
            $"it must be re-placed and re-paid. PayPal reported: {message}", issue);
    }

    private static PaymentGatewayException TranslateVoid(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error err))
        {
            var (message, issue) = Describe(err);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetNoContent(out RawError nc))
            return new PaymentGatewayException($"PayPal could not release the hold (HTTP {(int)nc.StatusCode}).", (int)nc.StatusCode);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal could not release the hold (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal could not release the hold.", 502);
    }

    private static PaymentGatewayException TranslateRefund(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error err))
        {
            var (message, issue) = Describe(err);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetNoContent(out RawError nc))
            return new PaymentGatewayException($"PayPal could not refund the capture (HTTP {(int)nc.StatusCode}).", (int)nc.StatusCode);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal could not refund the capture (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal could not refund the capture.", 502);
    }

    private static PaymentGatewayException TranslateCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 err))
        {
            var (message, issue) = Describe(err);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal could not save the card (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal could not save the card.", 502);
    }

    private static PaymentGatewayException TranslateDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 err))
        {
            var (message, issue) = Describe(err);
            return new PaymentGatewayException(message, 422, issue);
        }
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PaymentGatewayException($"PayPal could not delete the saved card (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode);
        return new PaymentGatewayException("PayPal could not delete the saved card.", 502);
    }

    private static (string message, string? issue) Describe(Error err)
    {
        var issue = err.Details?.Select(d => d.Issue).FirstOrDefault(i => !string.IsNullOrEmpty(i));
        var description = err.Details?.Select(d => d.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d));
        return (BuildMessage(err.Message, description, issue), issue);
    }

    private static (string message, string? issue) Describe(Error1 err)
    {
        var issue = err.Details?.Select(d => d.Issue).FirstOrDefault(i => !string.IsNullOrEmpty(i));
        var description = err.Details?.Select(d => d.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d));
        return (BuildMessage(err.Message, description, issue), issue);
    }

    private static string BuildMessage(string? message, string? description, string? issue)
    {
        var baseMessage = !string.IsNullOrEmpty(message) ? message! : "PayPal rejected the request.";
        if (!string.IsNullOrEmpty(description) && !string.Equals(description, baseMessage, StringComparison.Ordinal))
            baseMessage = $"{baseMessage} {description}";
        if (!string.IsNullOrEmpty(issue))
            baseMessage = $"{baseMessage} (issue: {issue})";
        return baseMessage;
    }

    private static bool IsExpiredAuthorization(string? issue, string? message)
    {
        bool Contains(string? s) => s is not null && s.IndexOf("EXPIR", StringComparison.OrdinalIgnoreCase) >= 0;
        return Contains(issue) || Contains(message);
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static PaymentGatewayException Transport(Exception ex) =>
        new PaymentGatewayException("The PayPal service is currently unreachable. Please try again.", ex, 503);

    private static PaymentGatewayException Malformed(Exception ex) =>
        new PaymentGatewayException("PayPal returned a response that could not be processed.", ex, 502);
}
