using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPayPalPaymentGateway"/> over the PayPal .NET SDK
/// (<c>AsadAli.Checkout.Sdk</c>). Every SDK type stays inside this class; callers see only the
/// application's plain payment contracts. Direct-card / server-side flow — no browser approval.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // Stable within one process run, distinct across restarts. Prefixed onto the create/authorize
    // idempotency keys so a double-click within a run stays idempotent, while a fresh run (whose
    // in-memory order ids restart at 1) never collides with an already-seen PayPal-Request-Id.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizeResult> AuthorizeWithCardAsync(PaymentAmount amount, string referenceId, CardPaymentDetails card, CancellationToken cancellationToken = default) =>
        Guard(() => CreateAndAuthorizeAsync(amount, referenceId, BuildCardRequest(card), cancellationToken));

    public Task<AuthorizeResult> AuthorizeWithVaultedCardAsync(PaymentAmount amount, string referenceId, string vaultId, CancellationToken cancellationToken = default) =>
        Guard(() => CreateAndAuthorizeAsync(amount, referenceId, new CardRequest { VaultId = vaultId }, cancellationToken));

    private async Task<AuthorizeResult> CreateAndAuthorizeAsync(PaymentAmount amount, string referenceId, CardRequest card, CancellationToken cancellationToken)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = referenceId,
                    // custom_id carries the eShop order reference for reconciliation. invoice_id is
                    // intentionally omitted: the merchant account enforces a globally-unique
                    // invoice_id, and it is not needed to line transactions up against orders.
                    CustomId = referenceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = amount.Currency,
                        Value = FormatAmount(amount.Value)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        Order created;
        try
        {
            // With a direct card supplied as the payment source, intent=AUTHORIZE processes the card
            // and places the hold at create time — so ask for the full representation and read the
            // authorization straight back.
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"eshop-{RunId}-create-{referenceId}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            ex.Error.TryGetError(out var e);
            throw Translate("create the payment order", e, ex.Error);
        }

        var orderId = created.Id ?? throw new PayPalGatewayException("PayPal did not return an order id.", 502, isClientError: false);

        var authorization = ExtractAuthorization(created.PurchaseUnits);
        OrderStatus? orderStatus = created.Status;

        // Fallback: if the hold wasn't placed at create time, authorize explicitly.
        if (authorization is null && created.Status != OrderStatus.PayerActionRequired)
        {
            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: orderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"eshop-{RunId}-authorize-{referenceId}",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Translate("authorize the payment", e, ex.Error);
            }

            authorization = ExtractAuthorization(authorized.PurchaseUnits);
            orderStatus = authorized.Status;
        }

        if (authorization is null)
        {
            // No hold was placed. If PayPal is waiting on the shopper to approve in a browser
            // (a challenge), surface it distinctly — this integration does not build an approval
            // round-trip.
            if (orderStatus is not null && orderStatus == OrderStatus.PayerActionRequired)
            {
                throw new PayPalChallengeRequiredException(
                    "PayPal requires the shopper to approve this card payment in a browser (payer action required). " +
                    "This integration does not perform a browser approval round-trip.");
            }

            throw new PayPalGatewayException(
                $"PayPal did not return an authorization for order {orderId} (order status: {orderStatus?.Value ?? "unknown"}).",
                502, isClientError: false);
        }

        return new AuthorizeResult(orderId, authorization);
    }

    public Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: cancellationToken);
                return MapAuthorization(auth);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Translate("read the authorization", e, ex.Error);
            }
        });

    public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, PaymentAmount amount, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = amount.Currency, Value = FormatAmount(amount.Value) }
                    },
                    prefer: "return=representation",
                    ct: cancellationToken);
                return MapAuthorization(auth);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                string? m = null, d = null;
                if (ex.Error.TryGetError(out var e)) { m = e.Message; d = e.DebugId; }
                // A reauthorization failure means the hold can no longer be honored — an operator
                // must create a fresh payment instead.
                throw new AuthorizationNoLongerHonorableException(
                    $"The authorization can no longer be renewed and the order cannot be fulfilled: {m ?? "PayPal rejected the reauthorization"}." +
                    (d is null ? string.Empty : $" (debug id {d})"),
                    ReadStatus(ex.Error), d);
            }
        });

    public Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default) =>
        Guard<object?>(async () =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: null,
                    prefer: "return=minimal",
                    ct: cancellationToken);
                return null;
            }
            catch (JsonException)
            {
                // A successful void returns 204 No Content; the SDK throws deserializing the empty
                // body into a PaymentAuthorization. An empty response here means the void succeeded.
                return null;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Translate("release the authorization", e, ex.Error);
            }
        });

    public Task<PayPalCapture> CaptureAsync(string authorizationId, PaymentAmount amount, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            try
            {
                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    ct: cancellationToken);

                var breakdown = captured.SellerReceivableBreakdown;
                var gross = ParseDecimal(breakdown?.GrossAmount?.Value)
                            ?? ParseDecimal(captured.Amount?.Value)
                            ?? amount.Value;

                return new PayPalCapture(
                    CaptureId: captured.Id ?? throw new PayPalGatewayException("PayPal did not return a capture id.", 502, isClientError: false),
                    Status: captured.Status?.Value ?? "UNKNOWN",
                    GrossAmount: gross,
                    PayPalFee: ParseDecimal(breakdown?.PaypalFee?.Value),
                    NetAmount: ParseDecimal(breakdown?.NetAmount?.Value),
                    Currency: captured.Amount?.CurrencyCode ?? amount.Currency);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Translate("capture the payment", e, ex.Error);
            }
        });

    public Task<PayPalRefund> RefundAsync(string captureId, PaymentAmount? amount, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            RefundRequest? body = amount is null
                ? null
                : new RefundRequest { Amount = new Money { CurrencyCode = amount.Currency, Value = FormatAmount(amount.Value) } };

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

                return new PayPalRefund(
                    RefundId: refund.Id ?? throw new PayPalGatewayException("PayPal did not return a refund id.", 502, isClientError: false),
                    Status: refund.Status?.Value ?? "UNKNOWN",
                    Amount: ParseDecimal(refund.Amount?.Value) ?? amount?.Value ?? 0m,
                    Currency: refund.Amount?.CurrencyCode ?? amount?.Currency ?? string.Empty);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Translate("refund the payment", e, ex.Error);
            }
        });

    public Task<VaultedCard> VaultCardAsync(CardPaymentDetails card, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            var request = new PaymentTokenRequest
            {
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = card.Number,
                        Expiry = FormatExpiry(card.ExpiryMonth, card.ExpiryYear),
                        SecurityCode = card.SecurityCode,
                        Name = card.CardholderName,
                        BillingAddress = BuildAddress(card.BillingAddress)
                    }
                }
            };

            try
            {
                var token = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: null,
                    body: request,
                    ct: cancellationToken);

                var cardEntity = token.PaymentSource?.Card;
                var vaultId = token.Id ?? throw new PayPalGatewayException("PayPal did not return a vault id for the saved card.", 502, isClientError: false);
                var (month, year) = SplitExpiry(cardEntity?.Expiry);

                return new VaultedCard(
                    VaultId: vaultId,
                    Brand: cardEntity?.Brand?.Value,
                    LastFourDigits: cardEntity?.LastDigits ?? LastFour(card.Number),
                    ExpiryMonth: month ?? NormalizeMonth(card.ExpiryMonth),
                    ExpiryYear: year ?? card.ExpiryYear);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                ex.Error.TryGetError1(out var e);
                throw Translate("save the card", e, ex.Error);
            }
        });

    public Task RemoveVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default) =>
        Guard<object?>(async () =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
                return null;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                ex.Error.TryGetError1(out var e);
                throw Translate("remove the saved card", e, ex.Error);
            }
        });

    // PayPal's transaction search requires start_date and end_date to be within 31 days of each
    // other, so a wider range is walked in windows. 30 days leaves a safety margin.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(30);

    public Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            var results = new List<GatewayTransaction>();
            var seen = new HashSet<string>();

            // Walk the whole range in <=30-day windows, and every page within each window, so the
            // report covers the entire range rather than just the first page/window.
            var windowStart = from;
            while (windowStart < to)
            {
                var windowEnd = windowStart + MaxSearchWindow;
                if (windowEnd > to) windowEnd = to;

                await CollectWindowAsync(windowStart, windowEnd, results, seen, cancellationToken);

                windowStart = windowEnd;
            }

            return (IReadOnlyList<GatewayTransaction>)results;
        });

    private async Task CollectWindowAsync(DateTimeOffset from, DateTimeOffset to, List<GatewayTransaction> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);

        int page = 1;
        int totalPages = 1;
        const int safetyCap = 1000;

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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                // SearchTransactions is the SDK's only Case-B operation (RawError directly).
                int status = (int)ex.Error.StatusCode;
                throw new PayPalGatewayException(
                    $"PayPal transaction search failed (HTTP {status}).",
                    status, isClientError: status is >= 400 and < 500);
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;

                    var id = info.TransactionId ?? string.Empty;
                    if (id.Length > 0 && !seen.Add(id)) continue; // guard against window-boundary dupes

                    results.Add(new GatewayTransaction(
                        TransactionId: id,
                        Status: info.TransactionStatus,
                        Amount: ParseDecimal(info.TransactionAmount?.Value),
                        Currency: info.TransactionAmount?.CurrencyCode,
                        FeeAmount: info.FeeAmount?.Value,
                        InitiationDate: ParseTime(info.TransactionInitiationDate),
                        ReferenceId: info.CustomField ?? info.InvoiceId,
                        EventCode: info.TransactionEventCode));
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages && page <= safetyCap);
    }

    // --- mapping helpers ---

    private static CardRequest BuildCardRequest(CardPaymentDetails card) => new()
    {
        Number = card.Number,
        Expiry = FormatExpiry(card.ExpiryMonth, card.ExpiryYear),
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static Address? BuildAddress(CardBillingAddress? a)
    {
        if (a is null || string.IsNullOrWhiteSpace(a.CountryCode)) return null;
        return new Address
        {
            AddressLine1 = a.AddressLine1,
            AddressLine2 = a.AddressLine2,
            AdminArea2 = a.AdminArea2,
            AdminArea1 = a.AdminArea1,
            PostalCode = a.PostalCode,
            CountryCode = a.CountryCode!
        };
    }

    private static PayPalAuthorization? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null) return null;
        foreach (var pu in purchaseUnits)
        {
            var auths = pu.Payments?.Authorizations;
            if (auths is null) continue;
            foreach (var a in auths)
            {
                if (a.Id is null) continue;
                return new PayPalAuthorization(a.Id, a.Status?.Value ?? "CREATED", ParseTime(a.ExpirationTime));
            }
        }
        return null;
    }

    private static PayPalAuthorization MapAuthorization(PaymentAuthorization auth) =>
        new(auth.Id ?? string.Empty, auth.Status?.Value ?? "UNKNOWN", ParseTime(auth.ExpirationTime));

    private static string FormatAmount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatExpiry(string month, string year) => $"{NormalizeYear(year)}-{NormalizeMonth(month)}";

    private static string NormalizeMonth(string month) => month.Trim().PadLeft(2, '0');

    private static string NormalizeYear(string year)
    {
        var y = year.Trim();
        return y.Length == 2 ? $"20{y}" : y;
    }

    private static (string? Month, string? Year) SplitExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) return (null, null);
        var parts = expiry.Split('-');
        return parts.Length == 2 ? (parts[1], parts[0]) : (null, null);
    }

    private static string LastFour(string number)
    {
        var digits = number.Trim();
        return digits.Length <= 4 ? digits : digits.Substring(digits.Length - 4);
    }

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    // --- error translation ---

    private static int? ReadStatus(ApiError apiError) =>
        apiError.TryGetRawError(out var raw) ? (int)raw.StatusCode : null;

    private PayPalGatewayException Translate(string action, Error? typed, ApiError apiError) =>
        TranslateCore(action, typed?.Message, typed?.DebugId, FormatDetails(typed?.Details), apiError);

    private PayPalGatewayException Translate(string action, Error1? typed, ApiError apiError) =>
        TranslateCore(action, typed?.Message, typed?.DebugId, FormatDetails(typed?.Details), apiError);

    private static string? FormatDetails(IReadOnlyList<ErrorDetails>? details) =>
        details is null || details.Count == 0
            ? null
            : string.Join("; ", details.Select(d => $"{d.Issue}{(d.Description is null ? string.Empty : ": " + d.Description)}{(d.Field is null ? string.Empty : $" [{d.Field}]")}"));

    private static string? FormatDetails(IReadOnlyList<ErrorDetails1>? details) =>
        details is null || details.Count == 0
            ? null
            : string.Join("; ", details.Select(d => $"{d.Issue}{(d.Description is null ? string.Empty : ": " + d.Description)}{(d.Field is null ? string.Empty : $" [{d.Field}]")}"));

    private PayPalGatewayException TranslateCore(string action, string? typedMessage, string? debugId, string? details, ApiError apiError)
    {
        int? status = null;
        string? rawBody = null;
        if (apiError.TryGetRawError(out var raw))
        {
            status = (int)raw.StatusCode;
            try { rawBody = raw.ReadAsString(); } catch { /* body may be non-text */ }
        }

        var message = typedMessage ?? rawBody;
        if (details is not null)
        {
            message = message is null ? details : $"{message} ({details})";
        }

        // A typed error body is present only for 4xx client rejections; treat those as caller-actionable.
        var clientError = typedMessage is not null || status is >= 400 and < 500;
        var effectiveStatus = status ?? (typedMessage is not null ? 422 : 502);

        var full = $"PayPal failed to {action}: {message ?? "unexpected error"}" +
                   (debugId is null ? string.Empty : $" (debug id {debugId})");
        _logger.LogWarning("PayPal error while trying to {Action}. Status={Status} DebugId={DebugId} Details={Details}", action, effectiveStatus, debugId, details);
        return new PayPalGatewayException(full, effectiveStatus, debugId, isClientError: clientError);
    }

    // --- transport / json guard shared by every call ---

    private async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (PayPalGatewayException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that didn't match its
            // generated error shape. Either way it is not a caller-fixable request.
            throw new PayPalGatewayException("PayPal returned a response that could not be processed.", 502, isClientError: false, inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new PayPalGatewayException("PayPal is currently unreachable. Please try again.", 502, isClientError: false, inner: ex);
        }
    }
}
