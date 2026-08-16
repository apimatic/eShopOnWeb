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
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single place that talks to PayPal. Translates the app's payment intents into PayPal SDK
/// calls and PayPal's responses/errors back into the app's own types — nothing outside this class
/// knows the SDK exists. Every failure leaves here as a <see cref="PaymentGatewayException"/> with
/// a caller-safe message; raw card details are never logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;

    public PayPalPaymentGateway(PayPalServerSdkClient client, PayPalSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalPaymentInstrument instrument, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest { Amount = AmountOf(amount) }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(instrument) }
        };

        var order = await InvokeAsync<Order, CreateOrderError>(
            ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct),
            TranslateCreateOrder, cancellationToken);

        EnsureNoChallenge(order.Status, order.Links);

        if (order.Id is null)
            throw new PaymentGatewayException("PayPal did not return an order id.", 502);

        // With a card supplied at creation and intent=AUTHORIZE, PayPal places the hold in the same
        // step, so the authorization is already on the create response. Only when it is absent (the
        // order was created without an inline authorization) do we authorize the order explicitly.
        var authorization = FindAuthorization(order.PurchaseUnits);
        if (authorization?.Id is null)
        {
            var authResponse = await InvokeAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
                ct => _client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey + "-auth",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct),
                TranslateAuthorizeOrder, cancellationToken);

            EnsureNoChallenge(authResponse.Status, null);
            authorization = FindAuthorization(authResponse.PurchaseUnits);
        }

        if (authorization?.Id is null)
            throw new PaymentGatewayException("PayPal did not return an authorization for the order.", 502);

        return new PayPalAuthorizationResult(
            order.Id,
            authorization.Id,
            authorization.Status?.Value ?? string.Empty,
            ParseDate(authorization.ExpirationTime));
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var captured = await InvokeAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct),
            TranslateCapture, cancellationToken);

        if (captured.Id is null)
            throw new PaymentGatewayException("PayPal did not return a capture id.", 502);

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(captured.Amount)
            ?? throw new PaymentGatewayException("PayPal did not report a captured amount.", 502);

        return new PayPalCaptureResult(
            captured.Id,
            captured.Status?.Value ?? string.Empty,
            gross,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var reauthorized = await InvokeAsync<PaymentAuthorization, ReauthorizePaymentError>(
            ct => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = MoneyOf(amount) },
                prefer: "return=representation",
                ct: ct),
            TranslateReauthorize, cancellationToken);

        if (reauthorized.Id is null)
            throw new PaymentGatewayException("PayPal did not return a renewed authorization.", 502);

        return new PayPalAuthorizationResult(
            null,
            reauthorized.Id,
            reauthorized.Status?.Value ?? string.Empty,
            ParseDate(reauthorized.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await InvokeAsync<PaymentAuthorization, VoidPaymentError>(
            ct => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: ct),
            TranslateVoid, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var refund = await InvokeAsync<Refund, RefundCapturedPaymentError>(
            ct => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: amount.HasValue ? new RefundRequest { Amount = MoneyOf(amount.Value) } : null,
                prefer: "return=representation",
                ct: ct),
            TranslateRefund, cancellationToken);

        if (refund.Id is null)
            throw new PaymentGatewayException("PayPal did not return a refund id.", 502);

        var refundedAmount = ParseMoney(refund.Amount) ?? amount ?? 0m;
        return new PayPalRefundResult(refund.Id, refund.Status?.Value ?? string.Empty, refundedAmount);
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        var token = await InvokeAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            ct => _client.Vault.CreatePaymentToken(
                payPalRequestId: Guid.NewGuid().ToString(),
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = BuildBillingAddress(card)
                        }
                    }
                },
                ct: ct),
            TranslateVaultCreate, cancellationToken);

        if (token.Id is null)
            throw new PaymentGatewayException("PayPal did not return a vault token.", 502);

        var cardInfo = token.PaymentSource?.Card;
        return new PayPalVaultedCardResult(
            token.Id,
            cardInfo?.Brand?.Value ?? "UNKNOWN",
            cardInfo?.LastDigits ?? "????",
            cardInfo?.Name ?? card.CardholderName,
            cardInfo?.Expiry ?? card.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await InvokeAsync<DeletePaymentTokenError>(
            ct => _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: ct),
            TranslateVaultDelete, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // PayPal reporting limits a single request to a 31-day span and rejects a start_date older
        // than ~3 years. Clamp the start to the queryable window, then walk the range in <=31-day
        // slices, paging each slice fully — so the report covers the whole range, not just page one.
        var floor = DateTimeOffset.Now.AddYears(-3).AddDays(1);
        var windowStart = from < floor ? floor : from;

        while (windowStart < to)
        {
            var windowEnd = windowStart.Add(MaxSearchWindow);
            if (windowEnd > to)
                windowEnd = to;

            await CollectTransactionsAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task CollectTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransactionRecord> results, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;

        do
        {
            var currentPage = page;
            var response = await InvokeAsync<SearchResponse, RawError>(
                ct => _client.TransactionSearch.SearchTransactions(
                    startDate: FormatDate(from),
                    endDate: FormatDate(to),
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
                    pageSize: SearchPageSize,
                    page: currentPage,
                    ct: ct),
                raw => FromRaw(raw), cancellationToken);

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                    continue;

                results.Add(new PayPalTransactionRecord(
                    info.TransactionId,
                    info.TransactionStatus ?? string.Empty,
                    ParseMoney(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    ParseDate(info.TransactionInitiationDate)));
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);
    }

    // ---------------- request/response mapping helpers ----------------

    private static CardRequest BuildCardRequest(PayPalPaymentInstrument instrument)
    {
        if (instrument.VaultTokenId is not null)
            return new CardRequest { VaultId = instrument.VaultTokenId };

        var card = instrument.Card
            ?? throw new PaymentOperationException("A card or a saved card is required to pay.");

        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildBillingAddress(card),
            // Only step up to a 3-D Secure/browser challenge when the issuer actually requires it,
            // so a plain sandbox card auth completes server-side.
            Attributes = new CardAttributes
            {
                Verification = new CardVerification { Method = OrdersCardVerificationMethod.ScaWhenRequired }
            }
        };
    }

    // AVS data. Values come from the card details when supplied, otherwise a US default — the task's
    // sandbox card accepts "any billing address".
    private static Address BuildBillingAddress(PayPalCardDetails card) => new()
    {
        AddressLine1 = card.BillingAddressLine1 ?? "1 Market St",
        AdminArea2 = card.BillingCity ?? "San Jose",
        AdminArea1 = card.BillingState ?? "CA",
        PostalCode = card.BillingPostalCode ?? "95131",
        CountryCode = card.BillingCountryCode ?? "US"
    };

    private AmountWithBreakdown AmountOf(decimal amount) => new()
    {
        CurrencyCode = _settings.Currency,
        Value = Format(amount)
    };

    private Money MoneyOf(decimal amount) => new()
    {
        CurrencyCode = _settings.Currency,
        Value = Format(amount)
    };

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is not null && decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// A card payment that needs browser approval (e.g. 3-D Secure) does not fail — it comes back
    /// with a payer-action status/link. We do not build an approval round-trip: we stop and surface it.
    /// </summary>
    private static void EnsureNoChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        var payerActionRequired = status == OrderStatus.PayerActionRequired;
        string? approvalUrl = null;

        if (links is not null)
        {
            foreach (var link in links)
            {
                var rel = link.Rel?.ToLowerInvariant() ?? string.Empty;
                if (rel.Contains("payer-action") || rel.Contains("approve"))
                {
                    payerActionRequired = true;
                    approvalUrl = link.Href;
                }
            }
        }

        if (payerActionRequired)
            throw new PaymentChallengeException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This server-side integration does not perform browser approval.", approvalUrl);
    }

    // ---------------- error translation ----------------

    private async Task<T> InvokeAsync<T, TError>(Func<CancellationToken, Task<T>> call,
        Func<TError, PaymentGatewayException> translate, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<TError> ex)
        {
            throw translate(ex.Error);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new PaymentGatewayException("PayPal could not be reached.", 504, ex);
        }
    }

    private async Task InvokeAsync<TError>(Func<CancellationToken, Task> call,
        Func<TError, PaymentGatewayException> translate, CancellationToken cancellationToken)
    {
        await InvokeAsync<bool, TError>(async ct => { await call(ct); return true; }, translate, cancellationToken);
    }

    private static PaymentGatewayException TranslateCreateOrder(CreateOrderError e) =>
        e.TryGetError(out var err) ? FromError(err)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateAuthorizeOrder(AuthorizeOrderError e) =>
        e.TryGetError(out var err) ? FromError(err)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateCapture(CaptureAuthorizedPaymentError e) =>
        e.TryGetError(out var err) ? FromError(err)
        : e.TryGetNoContent(out var noContent) ? FromRaw(noContent)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateReauthorize(ReauthorizePaymentError e) =>
        e.TryGetError(out var err) ? FromError(err)
        : e.TryGetNoContent(out var noContent) ? FromRaw(noContent)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateVoid(VoidPaymentError e) =>
        e.TryGetError(out var err) ? FromError(err)
        : e.TryGetNoContent(out var noContent) ? FromRaw(noContent)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateRefund(RefundCapturedPaymentError e) =>
        e.TryGetError(out var err) ? FromError(err)
        : e.TryGetNoContent(out var noContent) ? FromRaw(noContent)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateVaultCreate(CreatePaymentTokenError e) =>
        e.TryGetError1(out var err) ? FromError1(err)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException TranslateVaultDelete(DeletePaymentTokenError e) =>
        e.TryGetError1(out var err) ? FromError1(err)
        : e.TryGetRawError(out var raw) ? FromRaw(raw)
        : Generic();

    private static PaymentGatewayException FromError(Error error) =>
        new(Describe(error.Name, error.Message, error.Details?.Select(d => d.Issue + (d.Description is null ? "" : $" ({d.Description})"))), 422);

    private static PaymentGatewayException FromError1(Error1 error) =>
        new(Describe(error.Name, error.Message, error.Details?.Select(d => d.Issue + (d.Description is null ? "" : $" ({d.Description})"))), 422);

    private static PaymentGatewayException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        var caller = status is >= 400 and < 500 ? status : 502;
        return new PaymentGatewayException($"PayPal rejected the request (HTTP {status}).", caller);
    }

    private static PaymentGatewayException Generic() =>
        new("PayPal rejected the request.", 422);

    private static string Describe(string name, string message, IEnumerable<string>? issues)
    {
        var detail = issues is null ? null : string.Join("; ", issues);
        return string.IsNullOrEmpty(detail)
            ? $"PayPal: {name} — {message}"
            : $"PayPal: {name} — {message} [{detail}]";
    }
}
