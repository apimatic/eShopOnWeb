using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using DomainCardDetails = Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal.CardDetails;
using SdkAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single boundary to PayPal. Every SDK call is wrapped so that PayPal SDK types and failures
/// are translated into the plain domain contracts / payment exceptions — nothing SDK-shaped, and no
/// raw provider or serializer text, escapes this class. Full card numbers pass through only to
/// PayPal and are never stored or logged here.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentService> _logger;

    // A whole-call budget layered over the SDK's per-attempt timeout, so a hung provider cannot
    // pin a request indefinitely.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    public PayPalPaymentService(PayPalServerSdkClient client, IAppLogger<PayPalPaymentService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardPaymentRequest request, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);
        var token = cts.Token;

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatAmount(request.Amount)
                    },
                    // Stamp the eShop order id so the reconciliation report can line this up. Only
                    // custom_id is used (it surfaces as custom_field in transaction reporting); invoice_id
                    // is left unset because PayPal enforces its uniqueness per merchant across all time.
                    CustomId = request.OrderReference
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = BuildCardPaymentSource(request)
            }
        };

        global::PayPalServerSdk.Models.Order order;
        try
        {
            order = await _client.Orders.CreateOrder(null, request.IdempotencyKey, null, null, null,
                body, prefer: "return=representation", ct: token);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejected("authorize the payment", e);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("authorize the payment", raw);
            throw new PaymentProviderException("The payment could not be authorized.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("authorize the payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("authorize the payment", ex);
        }

        // A card that needs a browser approval/challenge cannot be driven card-only: stop, don't
        // build an approval round-trip.
        var status = order.Status?.Value;
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentRejectedException(
                "PayPal requires the shopper to approve this card payment in a browser " +
                "(PAYER_ACTION_REQUIRED). This card cannot be charged without that approval step.");
        }

        var authorization = ExtractAuthorization(order.PurchaseUnits);
        if (authorization is null)
        {
            // Defensive fallback: the authorization was not inlined — authorize the order explicitly.
            authorization = await AuthorizeExistingOrderAsync(order.Id!, request.IdempotencyKey, token);
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentProviderException("PayPal did not return an authorization for the order.");
        }

        return new AuthorizationResult(order.Id!, authorization.Id!, authorization.Status?.Value ?? "CREATED",
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(authorizationId, null, idempotencyKey,
                null, body: null, prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                if (IndicatesExpiredAuthorization(e))
                {
                    throw new PaymentAuthorizationExpiredException(Describe(e));
                }
                throw Rejected("capture the payment", e);
            }
            if (ex.Error.TryGetNoContent(out var nc)) throw FromRaw("capture the payment", nc);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("capture the payment", raw);
            throw new PaymentProviderException("The payment could not be captured.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("capture the payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("capture the payment", ex);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = ParseMoney(captured.Amount)
            ?? ParseMoney(breakdown?.GrossAmount)
            ?? 0m;

        return new CaptureResult(
            captured.Id ?? string.Empty,
            captured.Status?.Value ?? "COMPLETED",
            capturedAmount,
            captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.ReauthorizePayment(authorizationId, idempotencyKey, null, body,
                prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A non-reauthorizable hold is a rejection the operator must see verbatim.
            if (ex.Error.TryGetError(out var e)) throw Rejected("renew the authorization", e);
            if (ex.Error.TryGetNoContent(out var nc)) throw FromRaw("renew the authorization", nc);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("renew the authorization", raw);
            throw new PaymentRejectedException("The authorization could not be renewed.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("renew the authorization", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("renew the authorization", ex);
        }

        return new AuthorizationResult(string.Empty, auth.Id ?? authorizationId,
            auth.Status?.Value ?? "CREATED", ParseDate(auth.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

        try
        {
            // Note the parameter order: idempotency key is the 4th argument on VoidPayment. Ask for
            // a representation so PayPal returns a parseable body instead of a bare 204 No Content
            // (which the SDK cannot deserialize into PaymentAuthorization).
            await _client.Payments.VoidPayment(authorizationId, null, null, idempotencyKey,
                prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejected("release the held funds", e);
            if (ex.Error.TryGetNoContent(out var nc)) throw FromRaw("release the held funds", nc);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("release the held funds", raw);
            throw new PaymentProviderException("The held funds could not be released.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("release the held funds", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("release the held funds", ex);
        }
    }

    public async Task<decimal> GetCapturedAmountAsync(string captureId, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.GetCapturedPayment(captureId, null, ct: cts.Token);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejected("read the captured payment", e);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("read the captured payment", raw);
            throw new PaymentProviderException("The captured payment could not be read.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("read the captured payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("read the captured payment", ex);
        }

        return ParseMoney(captured.Amount)
            ?? ParseMoney(captured.SellerReceivableBreakdown?.GrossAmount)
            ?? 0m;
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

        // Full refund ⇒ null body; partial ⇒ explicit amount.
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : null;

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(captureId, null, idempotencyKey, null,
                body, prefer: "return=representation", ct: cts.Token);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejected("refund the payment", e);
            if (ex.Error.TryGetNoContent(out var nc)) throw FromRaw("refund the payment", nc);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("refund the payment", raw);
            throw new PaymentProviderException("The payment could not be refunded.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("refund the payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("refund the payment", ex);
        }

        var refundedAmount = ParseMoney(refund.Amount) ?? amount ?? 0m;
        return new RefundResult(refund.Id ?? string.Empty, refund.Status?.Value ?? "COMPLETED",
            refundedAmount, refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultCardResult> VaultCardAsync(DomainCardDetails card, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

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
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(null, body, ct: cts.Token);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var e)) throw Rejected("save the card", e);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("save the card", raw);
            throw new PaymentProviderException("The card could not be saved.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("save the card", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("save the card", ex);
        }

        var cardEntity = response.PaymentSource?.Card;
        // Safe descriptor only — never the PAN. Fall back to the caller's own inputs if PayPal omits a field.
        var lastFour = cardEntity?.LastDigits ?? LastFour(card.Number);
        return new VaultCardResult(
            response.Id ?? string.Empty,
            cardEntity?.Brand?.Value ?? "CARD",
            lastFour,
            cardEntity?.Expiry ?? card.Expiry,
            cardEntity?.Name ?? card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);

        try
        {
            await _client.Vault.DeletePaymentToken(vaultId, ct: cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var e)) throw Rejected("remove the saved card", e);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("remove the saved card", raw);
            throw new PaymentProviderException("The saved card could not be removed.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("remove the saved card", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("remove the saved card", ex);
        }
    }

    // PayPal transaction search allows at most a 31-day window per request and rejects future dates.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(30);

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        using var cts = LinkedBudget(ct);
        var token = cts.Token;

        var results = new List<PayPalTransactionRecord>();

        // Clamp the end to "now": PayPal rejects future end dates, and there can be no records there.
        var now = DateTimeOffset.UtcNow;
        var effectiveTo = to > now ? now : to;
        if (from >= effectiveTo)
        {
            // A future or empty range yields no PayPal records — a valid empty report, not a gap.
            return results;
        }

        // Walk the whole range in <=30-day windows so nothing beyond the first 31 days is missed.
        var windowStart = from;
        while (windowStart < effectiveTo)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > effectiveTo)
            {
                windowEnd = effectiveTo;
            }

            await SearchWindowAsync(windowStart, windowEnd, results, token);
            windowStart = windowEnd;
        }

        return results;
    }

    // Paginate a single <=30-day window, appending every page's transactions.
    private async Task SearchWindowAsync(DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransactionRecord> results, CancellationToken token)
    {
        var page = 1;
        int totalPages;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: FormatDateTime(from),
                    endDate: FormatDateTime(to),
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
                    ct: token);
            }
            catch (SdkException<RawError> ex)
            {
                // TransactionSearch is the one Case-B operation: the error model IS RawError.
                throw FromRaw("build the reconciliation report", ex.Error);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw Transport("build the reconciliation report", ex);
            }
            catch (JsonException ex)
            {
                throw Malformed("build the reconciliation report", ex);
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalTransactionRecord(
                        info.TransactionId ?? string.Empty,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseDate(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);
    }

    // ---- helpers ----

    private async Task<AuthorizationWithAdditionalData?> AuthorizeExistingOrderAsync(string orderId,
        string idempotencyKey, CancellationToken ct)
    {
        OrderAuthorizeResponse response;
        try
        {
            response = await _client.Orders.AuthorizeOrder(orderId, null, idempotencyKey, null, null,
                body: null, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var e)) throw Rejected("authorize the payment", e);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw("authorize the payment", raw);
            throw new PaymentProviderException("The payment could not be authorized.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Transport("authorize the payment", ex);
        }
        catch (JsonException ex)
        {
            throw Malformed("authorize the payment", ex);
        }

        return ExtractAuthorization(response.PurchaseUnits);
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(
        IEnumerable<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }

        foreach (var unit in purchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is null)
            {
                continue;
            }

            foreach (var authorization in authorizations)
            {
                if (!string.IsNullOrEmpty(authorization.Id))
                {
                    return authorization;
                }
            }
        }

        return null;
    }

    private static CardRequest BuildCardPaymentSource(AuthorizeCardPaymentRequest request)
    {
        if (request.VaultId is not null)
        {
            // Pay with a saved card: reference the vault id only, never raw card data.
            return new CardRequest { VaultId = request.VaultId };
        }

        var card = request.Card!;
        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card)
        };
    }

    private static SdkAddress? BuildAddress(DomainCardDetails card)
    {
        if (string.IsNullOrEmpty(card.BillingAddressLine1) && string.IsNullOrEmpty(card.PostalCode))
        {
            // Country is still required by PayPal — send a minimal address carrying it.
            return new SdkAddress { CountryCode = card.CountryCode };
        }

        return new SdkAddress
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private CancellationTokenSource LinkedBudget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }

    private static string LastFour(string number)
    {
        var digitsOnly = number.Replace(" ", string.Empty).Replace("-", string.Empty);
        return digitsOnly.Length >= 4 ? digitsOnly[^4..] : digitsOnly;
    }

    private static bool IndicatesExpiredAuthorization(Error error)
    {
        if (error.Name is not null && error.Name.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (error.Details is null)
        {
            return false;
        }

        foreach (var detail in error.Details)
        {
            if (detail.Issue is not null && detail.Issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(Error error)
    {
        var reason = error.Name ?? "error";
        if (error.Details is not null)
        {
            foreach (var detail in error.Details)
            {
                if (!string.IsNullOrEmpty(detail.Issue))
                {
                    reason += $" ({detail.Issue})";
                    break;
                }
            }
        }
        return reason;
    }

    private static string Describe(Error1 error)
    {
        var reason = error.Name ?? "error";
        if (error.Details is not null)
        {
            foreach (var detail in error.Details)
            {
                if (!string.IsNullOrEmpty(detail.Issue))
                {
                    reason += $" ({detail.Issue})";
                    break;
                }
            }
        }
        return reason;
    }

    private PaymentRejectedException Rejected(string action, Error error)
    {
        _logger.LogWarning($"PayPal rejected attempt to {action}: {error.DebugId}");
        return new PaymentRejectedException($"PayPal declined to {action}: {Describe(error)}.");
    }

    private PaymentRejectedException Rejected(string action, Error1 error)
    {
        _logger.LogWarning($"PayPal rejected attempt to {action}: {error.DebugId}");
        return new PaymentRejectedException($"PayPal declined to {action}: {Describe(error)}.");
    }

    private Exception FromRaw(string action, RawError raw)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning($"PayPal returned HTTP {status} while trying to {action}.");
        if (status is >= 400 and < 500)
        {
            return new PaymentRejectedException($"PayPal declined to {action} (HTTP {status}).");
        }
        return new PaymentProviderException($"PayPal could not {action} right now (HTTP {status}).");
    }

    private PaymentProviderException Transport(string action, Exception ex)
    {
        _logger.LogWarning($"PayPal was unreachable while trying to {action}: {ex.GetType().Name}");
        return new PaymentProviderException($"PayPal is currently unreachable, so it could not {action}.", ex);
    }

    private PaymentProviderException Malformed(string action, Exception ex)
    {
        _logger.LogWarning($"PayPal returned an unreadable response while trying to {action}.");
        return new PaymentProviderException(
            $"PayPal returned a response that could not be processed while trying to {action}.", ex);
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException;
}
